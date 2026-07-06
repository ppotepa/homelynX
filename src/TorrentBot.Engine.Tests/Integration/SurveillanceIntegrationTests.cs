using TorrentBot.Acl;
using TorrentBot.Bootstrap;
using TorrentBot.Adapters.Cli;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Integrations.Fakes;

namespace TorrentBot.Engine.Tests.Integration;

[Collection("FullStack")]
public sealed class SurveillanceIntegrationTests
{
    [Fact]
    public async Task Surveillance_stats_capability_returns_structured_events_via_engine()
    {
        await using var scope = await StartEngineAsync();
        var result = await scope.Engine.SubmitAsync(Invocation("surveillance.stats", new Dictionary<string, object?> { ["hours"] = "24h" }));

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.CapabilityResult!.Data);
        Assert.True((int)data["event_count"]! > 0);
    }

    [Fact]
    public async Task Surveillance_events_query_source_is_registered()
    {
        await using var scope = await StartEngineAsync();
        Assert.Contains(scope.Engine.GetQuerySourceManifests(), m => m.Name == "surveillance_events");
    }

    private static async Task<EngineScope> StartEngineAsync()
    {
        var engine = EngineBootstrap.Create(surveillancePlugin: new TorrentBot.Plugins.Surveillance.SurveillancePlugin(new FakeSurveillanceClient()));
        await engine.StartAsync();
        return new EngineScope(engine);
    }

    private static Invocation Invocation(string capability, IReadOnlyDictionary<string, object?>? parameters = null) =>
        new()
        {
            IsExplicit = true,
            CapabilityName = capability,
            Parameters = parameters,
            RequestContext = new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "admin", source: "test"),
            User = new AclService().ResolveUser("admin")
        };

    private sealed class EngineScope(EngineHost engine) : IAsyncDisposable
    {
        public EngineHost Engine => engine;
        public async ValueTask DisposeAsync() => await engine.StopAsync();
    }
}