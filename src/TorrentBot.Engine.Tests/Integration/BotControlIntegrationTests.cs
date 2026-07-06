using TorrentBot.Acl;
using TorrentBot.Bootstrap;
using TorrentBot.Adapters.Cli;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Integrations.Fakes;

namespace TorrentBot.Engine.Tests.Integration;

[Collection("FullStack")]
public sealed class BotControlIntegrationTests
{
    [Fact]
    public async Task Coord_status_capability_returns_online_service_via_engine()
    {
        await using var scope = await StartEngineAsync();
        var result = await scope.Engine.SubmitAsync(Invocation("coord.status"));

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.CapabilityResult!.Data);
        Assert.True((bool)data["online"]!);
        Assert.Equal("coord-input", data["service"]?.ToString());
    }

    [Fact]
    public async Task Bot_diag_capability_returns_engine_diagnostics()
    {
        await using var scope = await StartEngineAsync();
        var result = await scope.Engine.SubmitAsync(Invocation("bot.diag"));
        Assert.True(result.Success, result.Error);
        Assert.Contains("diagnostics", result.CapabilityResult!.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<EngineScope> StartEngineAsync()
    {
        var engine = EngineBootstrap.Create(botControlPlugin: new TorrentBot.Plugins.BotControl.BotControlPlugin(new FakeCoordInputClient()));
        await engine.StartAsync();
        return new EngineScope(engine);
    }

    private static Invocation Invocation(string capability) =>
        new()
        {
            IsExplicit = true,
            CapabilityName = capability,
            RequestContext = new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "admin", source: "test"),
            User = new AclService().ResolveUser("admin")
        };

    private sealed class EngineScope(EngineHost engine) : IAsyncDisposable
    {
        public EngineHost Engine => engine;
        public async ValueTask DisposeAsync() => await engine.StopAsync();
    }
}