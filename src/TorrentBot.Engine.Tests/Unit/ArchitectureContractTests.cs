using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Conversation;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ArchitectureContractTests
{
    [Fact]
    public async Task Engine_exposes_registered_capability_contracts_for_routing_and_help()
    {
        await using var scope = await StartEngineAsync();
        var contracts = scope.Engine.GetCapabilityContracts();
        Assert.Contains(contracts, c => c.Name == "torrent.search");
        Assert.Contains(contracts, c => c.Name == "download.start");
        Assert.Contains(contracts, c => c.Name == "download.start_media");
        Assert.Contains(contracts, c => c.Name == "query.execute");
        Assert.Contains(contracts, c => c.Name == "system.health");
        Assert.All(contracts, c => Assert.False(string.IsNullOrWhiteSpace(c.ExactSemantics)));
    }

    [Fact]
    public void ConversationContext_resolve_pending_action_removes_pending_and_builds_parameters()
    {
        var contract = new CapabilityContract(
            "torrent.search",
            "search torrents",
            [],
            RiskLevel.Safe,
            Continuations:
            [
                new ContinuationRule(
                    "on_success",
                    "await_indexed_choice",
                    new ExpectedResponseShape("index", "index"),
                    "torrent.select_result")
            ]);

        var context = new ConversationContext("s1", "u1");
        const string token = "tok-search";
        context.AddPendingAction(new PendingUserAction(
            token,
            "torrent.select_result",
            contract,
            new ExpectedResponseShape("index", "index"),
            Parameters: new Dictionary<string, object?> { ["query"] = "ubuntu" }));

        var resolution = context.ResolvePendingAction(
            token,
            new UserResponse(token, "u1", "select", "2", new Dictionary<string, object?> { ["index"] = 2 }));

        Assert.True(resolution.Resolved);
        Assert.Equal(2, resolution.Parameters!["index"]);
        Assert.Equal("ubuntu", resolution.Parameters!["query"]);
        Assert.Empty(context.PendingActions);
    }

    private static async Task<EngineScope> StartEngineAsync()
    {
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        return new EngineScope(engine);
    }

    private sealed class EngineScope(Engine.EngineHost engine) : IAsyncDisposable
    {
        public Engine.EngineHost Engine => engine;
        public async ValueTask DisposeAsync() => await engine.StopAsync();
    }
}
