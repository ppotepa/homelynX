using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Conversation;
using TorrentBot.Llm;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ArchitectureContractTests
{
    [Fact]
    public async Task Engine_exposes_registered_capability_contracts_for_planning()
    {
        await using var scope = await StartEngineAsync();
        var contracts = scope.Engine.GetCapabilityContracts();
        Assert.Contains(contracts, c => c.Name == "torrent.search");
        Assert.Contains(contracts, c => c.Name == "download.start");
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
        var token = "tok-search";
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
        Assert.Null(resolution.NewPendingActions);
        Assert.Equal(2, resolution.Parameters!["index"]);
        Assert.Equal("ubuntu", resolution.Parameters!["query"]);
        Assert.Empty(context.PendingActions);
    }

    [Fact]
    public async Task Planner_prompt_includes_contract_and_pending_sections_without_critical_rules()
    {
        await using var scope = await StartEngineAsync();
        var conversation = scope.Engine.ConversationContextStore!.GetOrCreate("chat-1", "admin");
        conversation.AddPendingAction(new PendingUserAction(
            "pending-1",
            "download.start",
            DownloadContract(),
            new ExpectedResponseShape("yes_no")));

        var prompt = LlmSystemPromptBuilder.BuildPlannerPrompt(new LlmPlanningRequest(
            "start download",
            [],
            [],
            Conversation: conversation,
            Contracts: scope.Engine.GetCapabilityContracts()));

        Assert.Contains("Tool & Capability Contracts", prompt, StringComparison.Ordinal);
        Assert.Contains("Pending Actions", prompt, StringComparison.Ordinal);
        Assert.Contains("pending-1", prompt, StringComparison.Ordinal);
        Assert.Contains("How to construct response", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("CRITICAL SEARCH RULE", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("CRITICAL FOLLOW-UP RULE", prompt, StringComparison.Ordinal);
    }

    private static CapabilityContract DownloadContract() =>
        new("download.start", "start download", [], RiskLevel.ConfirmationRequired);

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