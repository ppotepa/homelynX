using TorrentBot.Adapters.Telegram.Sdk;
using TorrentBot.Bootstrap;
using Telegram.Bot;

namespace TorrentBot.Adapters.Telegram.Host;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine($"TorrentBot.Adapters.Telegram.Host starting (args: {string.Join(' ', args)})");

        if (args.Contains("--harness", StringComparer.OrdinalIgnoreCase))
        {
            var harnessExit = await TelegramHostHarness.RunAsync().ConfigureAwait(false);
            Console.WriteLine($"Program.Main harness exit={harnessExit}");
            return harnessExit;
        }

        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
            ?? Environment.GetEnvironmentVariable("TORRENTBOT_TELEGRAM_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("No Telegram token configured; falling back to harness mode via Program.Main.");
            var fallbackExit = await TelegramHostHarness.RunAsync().ConfigureAwait(false);
            Console.WriteLine($"Program.Main fallback harness exit={fallbackExit}");
            return fallbackExit;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var engine = EngineBootstrap.Create();
        await engine.StartAsync(cts.Token).ConfigureAwait(false);
        await CapabilityManifestExporter.ExportIfConfiguredAsync(engine, cts.Token).ConfigureAwait(false);
        var client = new TelegramBotClient(token);
        var messenger = new TelegramBotSdkMessenger(client);
        var adapter = new TelegramProductionAdapter(engine, messenger);

        Console.WriteLine("TorrentBot Telegram host polling started.");
        client.StartReceiving(
            async (_, update, ct) => await adapter.HandleUpdateAsync(update, ct).ConfigureAwait(false),
            (_, exception, _) =>
            {
                Console.Error.WriteLine(exception);
                return Task.CompletedTask;
            },
            cancellationToken: cts.Token);

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await engine.StopAsync(cts.Token).ConfigureAwait(false);
        Console.WriteLine("Program.Main polling exit=0");
        return 0;
    }
}