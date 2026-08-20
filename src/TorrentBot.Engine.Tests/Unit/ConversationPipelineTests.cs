using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Conversation;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Engine.Conversation;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ConversationPipelineTests
{
    [Fact]
    public async Task ProcessUserResponseAsync_resolves_pending_and_executes_capability()
    {
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var services = PipelineBootstrap.Create(engine);
            var conversation = engine.ConversationContextStore!.GetOrCreate("chat-1", "admin");
            var contract = new CapabilityContract(
                "system.health",
                "health check",
                [],
                RiskLevel.Safe,
                UserInteractions: new UserInteractionSpec(ExpectedResponseTypes: ["yes_no"]));

            const string token = "pending-health";
            conversation.AddPendingAction(new PendingUserAction(
                token,
                "system.health",
                contract,
                new ExpectedResponseShape("yes_no")));

            var baseInvocation = new Invocation
            {
                RequestContext = new RequestContext("trace", "chat-1", "admin", source: "test"),
                User = new TorrentBot.Acl.AclService().ResolveUser("admin")
            };

            var result = await services.Conversation.ProcessUserResponseAsync(
                new UserResponse(token, "admin", "confirm", token),
                conversation,
                baseInvocation);

            Assert.NotNull(result);
            Assert.True(result!.Success);
            Assert.Empty(conversation.PendingActions.Where(a => a.Token == token));
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task RegisterPendingFromResult_adds_index_pending_after_search_results()
    {
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var services = PipelineBootstrap.Create(engine);
            var conversation = engine.ConversationContextStore!.GetOrCreate("chat-1", "admin");
            var contract = engine.GetCapabilityContracts().First(c => c.Name == "torrent.search");

            services.Conversation.RegisterPendingFromResult(
                conversation,
                "torrent.search",
                contract,
                new Dictionary<string, object?> { ["query"] = "ubuntu" },
                new CapabilityResult(
                    Success: true,
                    Data: new Dictionary<string, object?>
                    {
                        ["count"] = 2,
                        ["results"] = new List<object>(),
                        ["query"] = "ubuntu"
                    }));

            Assert.Single(conversation.PendingActions);
            Assert.Equal("torrent.select_result", conversation.PendingActions[0].CapabilityName);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public void Resolve_ordinary_text_does_not_consume_pending_selection()
    {
        var store = new TorrentBot.Engine.Context.ConversationContextStore();
        var conversation = store.GetOrCreate("chat-1", "admin");
        var contract = new CapabilityContract("torrent.search", "search", [], RiskLevel.Safe);
        conversation.AddPendingAction(new PendingUserAction(
            "token-1",
            "torrent.select_result",
            contract,
            new ExpectedResponseShape("index", "index")));

        var resolution = new ConversationResponseHandler(store)
            .Resolve("chat-1", "admin", callbackData: null, text: "wybierz drugi");

        Assert.False(resolution.Handled);
        Assert.Single(conversation.PendingActions);
    }

    [Fact]
    public void Resolve_select_callback_consumes_explicit_pending_response()
    {
        var store = new TorrentBot.Engine.Context.ConversationContextStore();
        var conversation = store.GetOrCreate("chat-1", "admin");
        var contract = new CapabilityContract("torrent.search", "search", [], RiskLevel.Safe);
        conversation.AddPendingAction(new PendingUserAction(
            "token-1",
            "torrent.select_result",
            contract,
            new ExpectedResponseShape("index", "index")));

        var resolution = new ConversationResponseHandler(store)
            .Resolve("chat-1", "admin", callbackData: "select:2");

        Assert.True(resolution.Handled);
        Assert.NotNull(resolution.UserResponse);
        Assert.Equal("select", resolution.UserResponse!.ResponseType);
        Assert.Equal(2, resolution.UserResponse.ParsedParameters!["index"]);
    }

    [Fact]
    public async Task ProcessUserResponseAsync_cancel_does_not_execute_capability()
    {
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var services = PipelineBootstrap.Create(engine);
            var conversation = engine.ConversationContextStore!.GetOrCreate("chat-1", "admin");
            var contract = new CapabilityContract(
                "torrent.delete",
                "delete torrent",
                [],
                RiskLevel.Destructive,
                UserInteractions: new UserInteractionSpec(ExpectedResponseTypes: ["yes_no"]));

            const string token = "pending-delete";
            conversation.AddPendingAction(new PendingUserAction(
                token,
                "torrent.delete",
                contract,
                new ExpectedResponseShape("yes_no"),
                Parameters: new Dictionary<string, object?> { ["hash"] = "abc" }));

            var baseInvocation = new Invocation
            {
                RequestContext = new RequestContext("trace", "chat-1", "admin", source: "test"),
                User = new TorrentBot.Acl.AclService().ResolveUser("admin")
            };

            var result = await services.Conversation.ProcessUserResponseAsync(
                new UserResponse(token, "admin", "cancel", token),
                conversation,
                baseInvocation);

            Assert.NotNull(result);
            Assert.False(result!.Success);
            Assert.Contains("cancelled", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(conversation.PendingActions);
        }
        finally
        {
            await engine.StopAsync();
        }
    }
}
