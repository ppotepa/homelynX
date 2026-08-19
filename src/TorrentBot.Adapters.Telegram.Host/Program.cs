using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Collections.Concurrent;
using TorrentBot.Adapters.Telegram.Sdk;
using TorrentBot.Bootstrap;
using TorrentBot.Plugins.Tools;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TorrentBot.Adapters.Telegram.Host;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--harness", StringComparer.OrdinalIgnoreCase))
        {
            return await TelegramHostHarness.RunAsync().ConfigureAwait(false);
        }

        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
            ?? Environment.GetEnvironmentVariable("TORRENTBOT_TELEGRAM_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            return await TelegramHostHarness.RunAsync().ConfigureAwait(false);
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var client = new TelegramBotClient(token);
        var messenger = new TelegramBotSdkMessenger(client);
        var engine = EngineBootstrap.Create(completionNotifier: new TelegramDownloadCompletionNotifier(messenger));
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options => options.SingleLine = true);
            builder.SetMinimumLevel(LogLevel.Information);
        });
        engine.SetLoggerFactory(loggerFactory);
        await engine.StartAsync(cts.Token).ConfigureAwait(false);
        await CapabilityManifestExporter.ExportIfConfiguredAsync(engine, cts.Token).ConfigureAwait(false);
        var adapter = new TelegramProductionAdapter(engine, messenger);

        Task? shortenerTask = null;
        Task? trackingTask = null;
        if (string.Equals(Environment.GetEnvironmentVariable("TORRENTBOT_SHORTENER_ENABLED"), "true", StringComparison.OrdinalIgnoreCase)
            && engine.GetService<ShortLinkService>() is { } shortLinks)
        {
            shortenerTask = Task.Run(() => RunShortenerEndpointAsync(args, shortLinks, cts.Token), cts.Token);
        }
        if (engine.GetService<TrackingMonitor>() is { } trackingMonitor)
        {
            trackingTask = Task.Run(() => trackingMonitor.RunAsync(async (shipment, update) =>
            {
                if (!long.TryParse(shipment.UserId, out var chatId)) return;
                var label = string.IsNullOrWhiteSpace(shipment.Label) ? shipment.Number : shipment.Label;
                await messenger.SendTextAsync(chatId, $"📦 {label} — {update.Status}\n{update.Description}", ct: cts.Token).ConfigureAwait(false);
            }, cts.Token), cts.Token);
        }

        _ = await client.GetMe(cts.Token).ConfigureAwait(false);

        var enableTestEndpoint = string.Equals(
            Environment.GetEnvironmentVariable("TORRENTBOT_ENABLE_TEST_ENDPOINT"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var testEndpointSecret = Environment.GetEnvironmentVariable("TORRENTBOT_TEST_ENDPOINT_SECRET");

        if (enableTestEndpoint)
        {
            if (string.IsNullOrWhiteSpace(testEndpointSecret))
            {
                Console.WriteLine(
                    "TORRENTBOT_ENABLE_TEST_ENDPOINT is true but TORRENTBOT_TEST_ENDPOINT_SECRET is not set; test endpoint disabled.");
            }
            else
            {
                _ = Task.Run(() => RunTestEndpointAsync(args, testEndpointSecret, cts.Token), cts.Token);
            }
        }

        var offset = 0;
        using var updateSlots = new SemaphoreSlim(8, 8);
        var updateTasks = new ConcurrentBag<Task>();
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var updates = await client.GetUpdates(offset, timeout: 10, cancellationToken: cts.Token).ConfigureAwait(false);
                foreach (var update in updates)
                {
                    offset = update.Id + 1;
                    // Do not let a long-running tool (for example chiptune rendering)
                    // block polling. Telegram commands must remain responsive.
                    updateTasks.Add(DispatchUpdateAsync(update, adapter, updateSlots, cts.Token));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(1000, cts.Token).ConfigureAwait(false);
            }
        }

        try { await Task.WhenAll(updateTasks).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }

        await engine.StopAsync(CancellationToken.None).ConfigureAwait(false);
        if (shortenerTask is not null)
        {
            try { await shortenerTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        if (trackingTask is not null)
        {
            try { await trackingTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        return 0;
    }

    private static async Task DispatchUpdateAsync(
        Update update,
        TelegramProductionAdapter adapter,
        SemaphoreSlim slots,
        CancellationToken cancellationToken)
    {
        try
        {
            await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await adapter.HandleUpdateAsync(update, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                slots.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[telegram] update {update.Id} failed: {ex}");
        }
    }

    private static async Task RunShortenerEndpointAsync(string[] args, ShortLinkService shortLinks, CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("TORRENTBOT_SHORTENER_BIND_URL") ?? "http://0.0.0.0:8089");
        var app = builder.Build();
        app.MapGet("/s/{code}", async (string code) =>
        {
            var link = await shortLinks.ResolveAsync(code, countVisit: true);
            if (link is null) return Results.NotFound(new { error = "short link not found" });
            if (!link.VisitAccepted || link.Disabled || link.Expires <= DateTimeOffset.UtcNow || link.MaxVisits is not null && link.Visits > link.MaxVisits)
                return Results.StatusCode(StatusCodes.Status410Gone);
            return Results.Redirect(link.Url, permanent: false, preserveMethod: false);
        });
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "shortener" }));
        await app.RunAsync(ct).ConfigureAwait(false);
    }

    private static async Task RunTestEndpointAsync(
        string[] args,
        string testEndpointSecret,
        CancellationToken ct)
    {
        var testEngine = EngineBootstrap.Create();
        await testEngine.StartAsync(ct).ConfigureAwait(false);

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://127.0.0.1:5000");
        var app = builder.Build();

        app.MapPost("/test/inject-update", async (HttpContext context) =>
        {
            if (!IsAuthorizedTestRequest(context, testEndpointSecret))
            {
                return Results.Unauthorized();
            }

            try
            {
                var updateJson = await new StreamReader(context.Request.Body).ReadToEndAsync(ct);
                var update = JsonSerializer.Deserialize<Update>(updateJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (update is null)
                {
                    return Results.BadRequest(new { error = "Invalid Update JSON" });
                }

                var recordingMessenger = new RecordingTelegramMessenger();
                var testAdapter = new TelegramProductionAdapter(testEngine, recordingMessenger);
                await testAdapter.HandleUpdateAsync(update, ct).ConfigureAwait(false);

                var responseText = recordingMessenger.Edited.Count > 0
                    ? recordingMessenger.Edited.Last().Text
                    : recordingMessenger.Sent.Count > 0
                        ? recordingMessenger.Sent.Last().Text
                        : "No response";

                return Results.Ok(new
                {
                    success = true,
                    response = responseText,
                    messagesSent = recordingMessenger.Sent.Count,
                    messagesEdited = recordingMessenger.Edited.Count,
                    allSent = recordingMessenger.Sent.Select(m => m.Text).ToList(),
                    allEdited = recordingMessenger.Edited.Select(m => m.Text).ToList()
                });
            }
            catch
            {
                return Results.StatusCode(500);
            }
        });

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        try
        {
            await app.RunAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await testEngine.StopAsync(ct).ConfigureAwait(false);
        }
    }

    private static bool IsAuthorizedTestRequest(HttpContext context, string expectedSecret)
    {
        var provided = context.Request.Headers["X-TorrentBot-Test-Secret"].FirstOrDefault()
            ?? context.Request.Headers.Authorization.FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
        return !string.IsNullOrWhiteSpace(provided)
            && string.Equals(provided, expectedSecret, StringComparison.Ordinal);
    }
}
