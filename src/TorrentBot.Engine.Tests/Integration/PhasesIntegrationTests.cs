using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TorrentBot.Adapters.Telegram;
using TorrentBot.Adapters.Telegram.Host;
using TorrentBot.Adapters.Telegram.Sdk;
using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Engine.Audit;
using TorrentBot.Engine.Migration;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Integrations.Models;
using TorrentBot.Engine.Tests.Support;
using TorrentBot.Llm;
using TorrentBot.Plugins.Downloads;
using TorrentBot.Plugins.Surveillance;
using TorrentBot.Engine.Notifications;

namespace TorrentBot.Engine.Tests.Integration;

[Collection("FullStack")]
public sealed class PhasesIntegrationTests
{
    [Fact]
    public async Task Telegram_host_harness_processes_health_update_twice()
    {
        var exitCode = await TelegramHostHarness.RunAsync();
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Bootstrap_selects_http_clients_when_env_configured()
    {
        Environment.SetEnvironmentVariable("JACKETT_URL", "http://jackett:9117");
        Environment.SetEnvironmentVariable("QBITTORRENT_URL", "http://qbittorrent:8080");
        try
        {
            var (jackett, qbit) = EngineBootstrap.CreateTorrentClients();
            Assert.IsType<Integrations.Clients.JackettClient>(jackett);
            Assert.IsType<Integrations.Clients.QBittorrentClient>(qbit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JACKETT_URL", null);
            Environment.SetEnvironmentVariable("QBITTORRENT_URL", null);
        }
    }

    [Fact]
    public void Bootstrap_resolves_homelynx_host_port_env_for_integrations()
    {
        Environment.SetEnvironmentVariable("JACKETT_URL", null);
        Environment.SetEnvironmentVariable("TORRENTBOT_JACKETT_URL", null);
        Environment.SetEnvironmentVariable("QBITTORRENT_URL", null);
        Environment.SetEnvironmentVariable("TORRENTBOT_QBITTORRENT_URL", null);
        Environment.SetEnvironmentVariable("JACKETT_HOST", "jackett");
        Environment.SetEnvironmentVariable("JACKETT_PORT", "9117");
        Environment.SetEnvironmentVariable("JACKETT_HTTPS", "false");
        Environment.SetEnvironmentVariable("QBIT_HOST", "qbittorrent");
        Environment.SetEnvironmentVariable("QBIT_PORT", "8080");
        try
        {
            var (jackett, qbit) = EngineBootstrap.CreateTorrentClients();
            Assert.IsType<Integrations.Clients.JackettClient>(jackett);
            Assert.IsType<Integrations.Clients.QBittorrentClient>(qbit);
        }
        finally
        {
            foreach (var key in new[]
                     {
                         "JACKETT_URL", "TORRENTBOT_JACKETT_URL", "QBITTORRENT_URL", "TORRENTBOT_QBITTORRENT_URL",
                         "JACKETT_HOST", "JACKETT_PORT", "JACKETT_HTTPS", "QBIT_HOST", "QBIT_PORT"
                     })
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }
    }

    [Fact]
    public async Task Tts_http_client_speaks_via_configured_url()
    {
        using var server = new HttpListener();
        server.Prefixes.Add("http://127.0.0.1:9878/");
        server.Start();
        using var serverCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!serverCts.IsCancellationRequested && server.IsListening)
            {
                var context = await server.GetContextAsync();
                var responseText = JsonSerializer.Serialize(new { audio_url = "http://piper-edge/speak/output.wav" });
                var buffer = Encoding.UTF8.GetBytes(responseText);
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(buffer, serverCts.Token);
                context.Response.Close();
            }
        }, serverCts.Token);

        Environment.SetEnvironmentVariable("TORRENTBOT_TTS_URL", "http://127.0.0.1:9878");
        try
        {
            var engine = EngineBootstrap.Create();
            await engine.StartAsync();
            try
            {
                var result = await engine.SubmitAsync(Invocation("tts.say", new Dictionary<string, object?> { ["text"] = "hello edge" }));
                Assert.True(result.Success, result.Error);
                var data = Assert.IsType<Dictionary<string, object?>>(result.CapabilityResult!.Data);
                Assert.Equal("http://piper-edge/speak/output.wav", data["audio_url"]?.ToString());
            }
            finally
            {
                await engine.StopAsync();
            }
        }
        finally
        {
            serverCts.Cancel();
            server.Stop();
            Environment.SetEnvironmentVariable("TORRENTBOT_TTS_URL", null);
        }
    }

    [Fact]
    public async Task Torrent_search_session_more_select_and_download_candidate_flow()
    {
        var jackett = new FakeJackettClient();
        jackett.SetResults(
        [
            new TorrentSearchResult("t1", "Ubuntu 24.04", "magnet:1", 1000, 50, "jackett"),
            new TorrentSearchResult("t2", "Ubuntu 22.04", "magnet:2", 900, 40, "jackett"),
            new TorrentSearchResult("t3", "Debian 12", "magnet:3", 800, 30, "jackett"),
            new TorrentSearchResult("t4", "Fedora 39", "magnet:4", 700, 25, "jackett"),
            new TorrentSearchResult("t5", "Mint 21", "magnet:5", 600, 20, "jackett"),
            new TorrentSearchResult("t6", "Arch ISO", "magnet:6", 500, 15, "jackett")
        ]);
        var qbit = new FakeQBittorrentClient();
        await using var scope = await StartEngineAsync(new DownloadsPlugin(jackett, qbit));

        var search = await scope.Engine.SubmitAsync(Invocation("torrent.search", new Dictionary<string, object?> { ["query"] = "ubuntu" }));
        Assert.True(search.Success, search.Error);

        var more = await scope.Engine.SubmitAsync(Invocation("torrent.more_results"));
        Assert.True(more.Success, more.Error);

        var selectPending = await scope.Engine.SubmitAsync(Invocation("torrent.select_result", new Dictionary<string, object?> { ["index"] = 0 }));
        var selectToken = ExtractConfirmationToken(selectPending);
        var select = await scope.Engine.SubmitAsync(Invocation("torrent.select_result", new Dictionary<string, object?>
        {
            ["index"] = 0,
            ["confirmationToken"] = selectToken
        }));
        Assert.True(select.Success, select.Error);

        var candidatePending = await scope.Engine.SubmitAsync(Invocation("torrent.download_candidate", new Dictionary<string, object?> { ["title"] = "Ubuntu 24.04" }));
        var candidateToken = ExtractConfirmationToken(candidatePending);
        var candidate = await scope.Engine.SubmitAsync(Invocation("torrent.download_candidate", new Dictionary<string, object?>
        {
            ["title"] = "Ubuntu 24.04",
            ["confirmationToken"] = candidateToken
        }));
        Assert.True(candidate.Success, candidate.Error);
    }

    [Fact]
    public async Task Torrent_cancel_search_clears_session_and_blocks_follow_up_actions()
    {
        var jackett = new FakeJackettClient();
        jackett.SetResults(
        [
            new TorrentSearchResult("t1", "Ubuntu 24.04", "magnet:1", 1000, 50, "jackett")
        ]);
        var qbit = new FakeQBittorrentClient();
        await using var scope = await StartEngineAsync(new DownloadsPlugin(jackett, qbit));

        var search = await scope.Engine.SubmitAsync(Invocation("torrent.search", new Dictionary<string, object?> { ["query"] = "ubuntu" }));
        Assert.True(search.Success, search.Error);

        var cancel = await scope.Engine.SubmitAsync(Invocation("torrent.cancel_search"));
        Assert.True(cancel.Success, cancel.Error);
        Assert.Contains("cleared", cancel.CapabilityResult?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var more = await scope.Engine.SubmitAsync(Invocation("torrent.more_results"));
        Assert.False(more.Success);
        Assert.Contains("No active torrent search session", more.Error ?? more.CapabilityResult?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_start_url_and_system_diagnostics_capabilities_work()
    {
        await using var scope = await StartEngineAsync();

        var urlPending = await scope.Engine.SubmitAsync(Invocation("download.start_url", new Dictionary<string, object?> { ["url"] = "https://example.com/file.iso" }));
        var urlToken = ExtractConfirmationToken(urlPending);
        var url = await scope.Engine.SubmitAsync(Invocation("download.start_url", new Dictionary<string, object?>
        {
            ["url"] = "https://example.com/file.iso",
            ["confirmationToken"] = urlToken
        }));
        Assert.True(url.Success, url.Error);

        var help = await scope.Engine.SubmitAsync(Invocation("system.help"));
        Assert.True(help.Success, help.Error);

        var llm = await scope.Engine.SubmitAsync(Invocation("system.llm_status"));
        Assert.True(llm.Success, llm.Error);

        var disk = await scope.Engine.SubmitAsync(Invocation("system.disk_usage"));
        Assert.True(disk.Success, disk.Error);

        var largeFiles = await scope.Engine.SubmitAsync(Invocation("system.find_large_files"));
        Assert.True(largeFiles.Success, largeFiles.Error);
    }

    [Fact]
    public async Task Jobs_list_and_cancel_via_engine_surface()
    {
        await using var scope = await StartEngineAsync();

        var startPending = await scope.Engine.SubmitAsync(Invocation("download.start", new Dictionary<string, object?>
        {
            ["provider"] = "url",
            ["url"] = "https://example.com/sample.bin"
        }));
        var startToken = ExtractConfirmationToken(startPending);
        var start = await scope.Engine.SubmitAsync(Invocation("download.start", new Dictionary<string, object?>
        {
            ["provider"] = "url",
            ["url"] = "https://example.com/sample.bin",
            ["confirmationToken"] = startToken
        }));
        Assert.True(start.Success, start.Error);

        var list = await scope.Engine.SubmitAsync(Invocation("jobs.list"));
        Assert.True(list.Success, list.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(list.CapabilityResult!.Data);
        var jobs = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(data["jobs"]);
        Assert.NotEmpty(jobs);

        var cancelPending = await scope.Engine.SubmitAsync(Invocation("jobs.cancel", new Dictionary<string, object?> { ["jobId"] = jobs[0]["id"] }));
        var cancelToken = ExtractConfirmationToken(cancelPending);
        var cancel = await scope.Engine.SubmitAsync(Invocation("jobs.cancel", new Dictionary<string, object?>
        {
            ["jobId"] = jobs[0]["id"],
            ["confirmationToken"] = cancelToken
        }));
        Assert.True(cancel.Success, cancel.Error);
    }

    [Fact]
    public async Task Llm_pipeline_writes_portal_audit_feature_types()
    {
        var audit = PortalAuditSink.CreateInMemory();
        var pipeline = new LlmPipeline(
            FixedPlanLlmPlanner.ActiveDownloads(),
            new AuditingLlmExecutor(new StubLlmExecutor(), audit),
            auditSink: audit);
        var engine = EngineBootstrap.Create(llmPipeline: pipeline, auditSink: audit);
        await engine.StartAsync();
        try
        {
            var invocation = new Invocation
            {
                IsExplicit = false,
                Text = "show active downloads",
                RequestContext = new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "admin", source: "test"),
                User = new Acl.AclService().ResolveUser("admin")
            };

            var result = await engine.SubmitAsync(invocation);
            Assert.True(result.Success, result.Error);
            Assert.True(audit.CountByAction("natural_intent") >= 1);
            Assert.True(audit.CountByAction("natural_plan") >= 1);
            Assert.True(audit.CountByAction("natural_step") >= 1);
            Assert.True(audit.CountByAction("natural_response") >= 1);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task Surveillance_http_client_fetches_media_when_url_configured()
    {
        var mediaBytes = Encoding.UTF8.GetBytes("http-surveillance-image-bytes");
        using var server = new HttpListener();
        server.Prefixes.Add("http://127.0.0.1:9876/");
        server.Start();
        using var serverCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!serverCts.IsCancellationRequested && server.IsListening)
            {
                var context = await server.GetContextAsync();
                if (context.Request.Url?.AbsolutePath.StartsWith("/media/", StringComparison.Ordinal) == true)
                {
                    context.Response.ContentType = "image/jpeg";
                    await context.Response.OutputStream.WriteAsync(mediaBytes, serverCts.Token);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }

                context.Response.Close();
            }
        }, serverCts.Token);

        Environment.SetEnvironmentVariable("TORRENTBOT_SURVEILLANCE_URL", "http://127.0.0.1:9876");
        try
        {
            var engine = EngineBootstrap.Create();
            await engine.StartAsync();
            try
            {
                var result = await engine.SubmitAsync(Invocation("surveillance.latest_snapshot"));
                Assert.True(result.Success, result.Error);
                var data = Assert.IsType<Dictionary<string, object?>>(result.CapabilityResult!.Data);
                Assert.Equal(true, data["deliverable"]);
                var decoded = Convert.FromBase64String(data["base64"]!.ToString()!);
                Assert.Equal(mediaBytes, decoded);
            }
            finally
            {
                await engine.StopAsync();
            }
        }
        finally
        {
            serverCts.Cancel();
            server.Stop();
            Environment.SetEnvironmentVariable("TORRENTBOT_SURVEILLANCE_URL", null);
        }
    }

    [Fact]
    public async Task Download_completion_triggers_notifier_via_job_runner()
    {
        var notifier = new RecordingDownloadCompletionNotifier();
        var engine = EngineBootstrap.Create(completionNotifier: notifier);
        await engine.StartAsync();
        try
        {
            engine.SeedCompletedJob(chatId: "99", userId: "admin");
            await Task.Delay(TimeSpan.FromSeconds(3));

            Assert.NotEmpty(notifier.Events);
            Assert.Equal("99", notifier.Events[0].ChatId);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task Torrent_search_returns_download_search_result_shape()
    {
        var jackett = new FakeJackettClient();
        jackett.SetResults([new TorrentSearchResult("Ubuntu", "magnet:1", null, 1000, 10, "jackett")]);
        await using var scope = await StartEngineAsync(new DownloadsPlugin(jackett, new FakeQBittorrentClient()));

        var search = await scope.Engine.SubmitAsync(Invocation("torrent.search", new Dictionary<string, object?> { ["query"] = "ubuntu" }));
        Assert.True(search.Success, search.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(search.CapabilityResult!.Data);
        Assert.Equal("search_results", data["artifactKind"]);
        var results = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(data["results"]);
        Assert.NotEmpty(results);
        Assert.Equal("Ubuntu", results[0]["name"]);
        Assert.Equal(1, results[0]["index"]);
    }

    [Fact]
    public async Task Legacy_python_delegation_shim_forwards_when_enabled()
    {
        using var server = new HttpListener();
        server.Prefixes.Add("http://127.0.0.1:8765/");
        server.Start();
        _ = Task.Run(async () =>
        {
            while (server.IsListening)
            {
                var context = await server.GetContextAsync();
                var responseText = JsonSerializer.Serialize(new { success = true, message = "delegated-ok" });
                var buffer = Encoding.UTF8.GetBytes(responseText);
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(buffer);
                context.Response.Close();
            }
        });

        Environment.SetEnvironmentVariable("TORRENTBOT_LEGACY_PYTHON_URL", "http://127.0.0.1:8765");
        Environment.SetEnvironmentVariable("TORRENTBOT_ENABLE_LEGACY_PYTHON", "true");
        Environment.SetEnvironmentVariable("TORRENTBOT_ENABLE_NEW_ENGINE", "true");
        try
        {
            var delegator = new HttpLegacyPythonDelegator(new HttpClient(), "http://127.0.0.1:8765");
            var engine = EngineBootstrap.Create(legacyDelegator: delegator);
            await engine.StartAsync();
            try
            {
                var result = await engine.SubmitAsync(new Invocation
                {
                    IsExplicit = true,
                    CapabilityName = "system.health",
                    RequestContext = new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "admin", source: "telegram"),
                    User = new Acl.AclService().ResolveUser("admin")
                });
                Assert.True(result.Success, result.Error);
                Assert.Contains("delegated", result.CapabilityResult?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                await engine.StopAsync();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TORRENTBOT_LEGACY_PYTHON_URL", null);
            Environment.SetEnvironmentVariable("TORRENTBOT_ENABLE_LEGACY_PYTHON", null);
            Environment.SetEnvironmentVariable("TORRENTBOT_ENABLE_NEW_ENGINE", null);
            server.Stop();
        }
    }

    [Fact]
    public async Task Telegram_production_adapter_delivers_surveillance_photo_on_media_result()
    {
        var messenger = new RecordingTelegramMessenger();
        var engine = EngineBootstrap.Create(surveillancePlugin: new SurveillancePlugin(new FakeSurveillanceClient()));
        await engine.StartAsync();
        try
        {
            var adapter = new TelegramProductionAdapter(engine, messenger);
            await adapter.HandleMappedUpdateAsync(
                new TelegramUpdate(42, "admin", "/latest", MessageId: 55),
                progressMessageId: 55);

            Assert.NotEmpty(messenger.Photos);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    private static async Task<EngineScope> StartEngineAsync(
        DownloadsPlugin? downloads = null,
        SurveillancePlugin? surveillance = null)
    {
        var engine = EngineBootstrap.Create(downloadsPlugin: downloads, surveillancePlugin: surveillance);
        await engine.StartAsync();
        return new EngineScope(engine);
    }

    private static string? ExtractConfirmationToken(ExecutionResult result)
    {
        if (result.CapabilityResult?.Data is not Dictionary<string, object?> data)
        {
            return null;
        }

        return data.TryGetValue("confirmationToken", out var token) ? token?.ToString() : null;
    }

    private static Invocation Invocation(string capability, IReadOnlyDictionary<string, object?>? parameters = null) =>
        new()
        {
            IsExplicit = true,
            CapabilityName = capability,
            Parameters = parameters,
            RequestContext = new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "admin", source: "test"),
            User = new Acl.AclService().ResolveUser("admin")
        };

    private sealed class EngineScope(EngineHost engine) : IAsyncDisposable
    {
        public EngineHost Engine => engine;
        public async ValueTask DisposeAsync() => await engine.StopAsync();
    }

}