using System.Text.Json;
using TorrentBot.Acl;
using TorrentBot.Bootstrap;
using TorrentBot.Adapters.Cli;
using TorrentBot.Adapters.Telegram;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Integrations.Models;
using TorrentBot.Engine.Confirmations;
using TorrentBot.Engine.Tests.Support;
using TorrentBot.Llm;
using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Engine.Tests.Integration;

[CollectionDefinition("FullStack", DisableParallelization = true)]
public sealed class FullStackIntegrationCollection
{
}

[Collection("FullStack")]
public sealed class FullStackIntegrationTests
{
    [Fact]
    public async Task Acl_denies_unauthorized_capability_for_guest()
    {
        await using var scope = await StartEngineAsync();
        var guest = new AclService().ResolveUser("guest");

        var result = await scope.Engine.SubmitAsync(new Invocation
        {
            IsExplicit = true,
            CapabilityName = "download.cancel",
            RequestContext = Request("guest"),
            User = guest
        });

        Assert.False(result.Success);
        Assert.Contains("denied", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Torrent_search_and_query_downloads_via_public_engine_surface()
    {
        var jackett = new FakeJackettClient();
        jackett.SetResults([new TorrentSearchResult("t1", "Ubuntu 24.04 ISO", "magnet:1", 4_000_000_000, 120, "jackett")]);
        var qbit = new FakeQBittorrentClient();
        await qbit.AddTorrentAsync(new AddTorrentRequest("magnet:?xt=urn:btih:ubuntu2404"));
        await using var scope = await StartEngineAsync(downloads: new DownloadsPlugin(jackett, qbit));

        var search = await scope.Engine.SubmitAsync(Invocation("download.search", new Dictionary<string, object?> { ["query"] = "ubuntu", ["provider"] = "torrent" }));
        Assert.True(search.Success, search.Error);
        var searchData = Assert.IsType<Dictionary<string, object?>>(search.CapabilityResult!.Data);
        Assert.True((int)searchData["count"]! > 0);

        var pausedHash = await qbit.AddTorrentAsync(new AddTorrentRequest("magnet:?xt=urn:btih:debian", Category: "linux"));
        await qbit.PauseAsync(pausedHash);

        var unfiltered = await scope.Engine.SubmitAsync(Invocation("query.execute", new Dictionary<string, object?>
        {
            ["source"] = "downloads"
        }));
        Assert.True(unfiltered.Success, unfiltered.Error);
        var allItems = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(
            Assert.IsType<Dictionary<string, object?>>(unfiltered.CapabilityResult!.Data)["items"]);
        Assert.True(allItems.Count >= 2);

        var query = await scope.Engine.SubmitAsync(Invocation("query.execute", new Dictionary<string, object?>
        {
            ["source"] = "downloads",
            ["where"] = new[] { new Dictionary<string, object?> { ["field"] = "status", ["op"] = "eq", ["value"] = "downloading" } }
        }));

        Assert.True(query.Success, query.Error);
        var queryData = Assert.IsType<Dictionary<string, object?>>(query.CapabilityResult!.Data);
        var items = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(queryData["items"]);
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.Equal("downloading", item["status"]?.ToString()));
        Assert.True(items.Count < allItems.Count);
    }

    [Fact]
    public async Task Natural_language_dry_run_plan_references_manifest_capabilities()
    {
        await using var scope = await StartEngineAsync();
        var pipeline = new LlmPipeline(FixedPlanLlmPlanner.ActiveDownloads(), new StubLlmExecutor());
        var plan = await pipeline.RunAsync(new LlmPipelineRequest(
            "are there any active downloads?",
            await LoadCapabilitiesAsync(scope.Engine),
            scope.Engine.GetQuerySourceManifests(),
            IsDryRun: true));

        Assert.True(plan.Execution.Success);
        Assert.Contains(plan.Plan.Steps, s => s.Capability == "query.execute");
        Assert.All(plan.Plan.Steps, s => Assert.Contains(s.Capability, new[] { "query.execute", "torrent.search", "system.health" }));
    }

    [Fact]
    public async Task Telegram_confirmation_callback_reexecutes_capability_with_token()
    {
        var confirmationStore = new ConfirmationStore();
        var pendingStore = new PendingInvocationStore();
        await using var scope = await StartEngineAsync(confirmationStore: confirmationStore);
        var host = new TelegramBotHost(
            scope.Engine,
            PipelineBootstrap.Create(scope.Engine, scope.Engine.LlmPipeline),
            confirmationStore: confirmationStore,
            pendingInvocationStore: pendingStore);
        var user = new AclService().ResolveUser("admin");

        var initial = await host.HandleUpdateAsync(
            new TelegramUpdate(1, user.UserId, "/cancel job:missing"),
            user);
        Assert.False(initial.Success);
        Assert.Contains("Confirmation required", initial.Message, StringComparison.OrdinalIgnoreCase);

        var token = ExtractConfirmationToken(initial.ExecutionResult);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var confirmed = await host.HandleUpdateAsync(
            new TelegramUpdate(1, user.UserId, CallbackData: $"confirm:{token}:download.cancel"),
            user);

        Assert.Contains("not found", confirmed.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Telegram_adapter_maps_slash_and_plain_text_through_orchestrator()
    {
        await using var scope = await StartEngineAsync();
        var llm = new LlmPipeline(FixedPlanLlmPlanner.HealthCheck(), new StubLlmExecutor());
        var host = new TelegramBotHost(scope.Engine, PipelineBootstrap.Create(scope.Engine, llm), llmPipeline: llm);
        var user = new AclService().ResolveUser("admin");

        var slash = await host.HandleUpdateAsync(new TelegramUpdate(1, user.UserId, "/health"), user);
        var plain = await host.HandleUpdateAsync(new TelegramUpdate(1, user.UserId, "system health check"), user);

        Assert.True(slash.Success);
        Assert.True(plain.Success);
        Assert.Contains("healthy", slash.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("healthy", plain.Message, StringComparison.OrdinalIgnoreCase);

        var slashStatus = ExtractHealthStatus(slash.ExecutionResult);
        var plainStatus = ExtractHealthStatus(plain.ExecutionResult);
        Assert.Equal("healthy", slashStatus);
        Assert.Equal(slashStatus, plainStatus);
    }

    [Fact]
    public async Task Cli_torrent_workflow_search_produces_structured_json_items()
    {
        var writer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(writer);
        try
        {
            var jackett = new FakeJackettClient();
            jackett.SetResults([new TorrentSearchResult("t1", "Ubuntu", "magnet:1", 1000, 10, "jackett")]);
            var exitCode = await CliApplication.RunAsync(
                ["capability", "call", "download.search", "--json", "--param", "query=ubuntu", "--param", "provider=torrent"],
                () => EngineBootstrap.Create(downloadsPlugin: new DownloadsPlugin(jackett)));
            Assert.Equal(0, exitCode);
            Assert.Contains("results", writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static async Task<EngineScope> StartEngineAsync(
        Action<EngineHost>? configure = null,
        DownloadsPlugin? downloads = null,
        ConfirmationStore? confirmationStore = null)
    {
        var engine = EngineBootstrap.Create(configure, downloadsPlugin: downloads, confirmationStore: confirmationStore);
        await engine.StartAsync();
        return new EngineScope(engine);
    }

    private static string? ExtractHealthStatus(ExecutionResult? result)
    {
        if (result?.CapabilityResult?.Data is not Dictionary<string, object?> data)
        {
            return null;
        }

        return data.TryGetValue("status", out var status) ? status?.ToString() : null;
    }

    private static string? ExtractConfirmationToken(ExecutionResult? result)
    {
        if (result?.CapabilityResult?.Data is not Dictionary<string, object?> data)
        {
            return null;
        }

        return data.TryGetValue("confirmationToken", out var token) ? token?.ToString() : null;
    }

    private static async Task<IReadOnlyList<CapabilityMetadata>> LoadCapabilitiesAsync(IEngine engine)
    {
        var result = await engine.SubmitAsync(Invocation("system.capabilities"));
        var data = Assert.IsType<Dictionary<string, object?>>(result.CapabilityResult!.Data);
        var list = Assert.IsAssignableFrom<List<Dictionary<string, object?>>>(data["capabilities"]);
        return list.Select(item => new CapabilityMetadata(
            item["name"]?.ToString() ?? "unknown",
            item.TryGetValue("command", out var cmd) ? cmd?.ToString() : null,
            item.TryGetValue("description", out var desc) ? desc?.ToString() ?? string.Empty : string.Empty,
            item.TryGetValue("permission", out var perm) ? perm?.ToString() ?? "USER" : "USER",
            RiskLevel.Safe)).ToList();
    }

    private static RequestContext Request(string userId) =>
        new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), userId, source: "test");

    private static Invocation Invocation(string capability, IReadOnlyDictionary<string, object?>? parameters = null) =>
        new()
        {
            IsExplicit = true,
            CapabilityName = capability,
            Parameters = parameters,
            RequestContext = Request("cli-user"),
            User = new AclService().ResolveUser("admin")
        };

    private sealed class EngineScope(EngineHost engine) : IAsyncDisposable
    {
        public EngineHost Engine => engine;
        public async ValueTask DisposeAsync() => await engine.StopAsync();
    }
}