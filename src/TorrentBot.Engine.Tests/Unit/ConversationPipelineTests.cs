using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Conversation;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Engine.Conversation;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Models;
using TorrentBot.Plugins.Downloads;

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
            var services = PipelineBootstrap.Create(engine, engine.LlmPipeline);
            var conversation = engine.ConversationContextStore!.GetOrCreate("chat-1", "admin");
            var contract = new CapabilityContract(
                "system.health",
                "health check",
                [],
                RiskLevel.Safe,
                UserInteractions: new UserInteractionSpec(ExpectedResponseTypes: ["yes_no"]));

            var token = "pending-health";
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
    public void RegisterPendingFromResult_adds_index_pending_after_search_results()
    {
        var engine = EngineBootstrap.Create();
        engine.StartAsync().GetAwaiter().GetResult();
        try
        {
            var services = PipelineBootstrap.Create(engine, engine.LlmPipeline);
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
            engine.StopAsync().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public async Task Integrated_nl_wybierz_drugi_resolves_pending_and_selects_second_torrent()
    {
        var jackett = new FakeJackettClient();
        jackett.SetResults(
        [
            new TorrentSearchResult("Alpha ISO", "magnet:1", null, 1000, 50, "jackett"),
            new TorrentSearchResult("Beta ISO", "magnet:2", null, 900, 40, "jackett")
        ]);
        var engine = EngineBootstrap.Create(downloadsPlugin: new DownloadsPlugin(jackett, new FakeQBittorrentClient()));
        await engine.StartAsync();
        try
        {
            var services = PipelineBootstrap.Create(engine, engine.LlmPipeline);
            const string chatId = "chat-1";
            var conversation = engine.ConversationContextStore!.GetOrCreate(chatId, "admin");
            var baseInvocation = new Invocation
            {
                IsDryRun = true,
                RequestContext = new RequestContext("trace", chatId, "admin", source: "test", chatId: chatId),
                User = new TorrentBot.Acl.AclService().ResolveUser("admin")
            };

            var searchResult = await services.Invocation.RunAsync(new Invocation
            {
                IsExplicit = true,
                IsDryRun = true,
                CapabilityName = "torrent.search",
                Parameters = new Dictionary<string, object?> { ["query"] = "ubuntu" },
                RequestContext = baseInvocation.RequestContext,
                User = baseInvocation.User
            });
            Assert.True(searchResult.Success, searchResult.Error);
            Assert.Single(conversation.PendingActions);
            Assert.Equal("torrent.select_result", conversation.PendingActions[0].CapabilityName);

            var handler = new ConversationResponseHandler(engine.ConversationContextStore);
            var resolution = handler.Resolve(chatId, "admin", callbackData: null, text: "wybierz drugi");
            Assert.True(resolution.Handled);
            Assert.NotNull(resolution.UserResponse);
            Assert.Equal("select", resolution.UserResponse!.ResponseType);
            Assert.Equal(2, resolution.UserResponse.ParsedParameters!["index"]);

            var selectResult = await services.Conversation.ProcessUserResponseAsync(
                resolution.UserResponse,
                conversation,
                baseInvocation);
            Assert.NotNull(selectResult);
            Assert.True(selectResult!.Success, selectResult.Error);

            var selected = ExtractSelected(selectResult.Artifacts.RawResult!);
            Assert.NotNull(selected);
            Assert.Equal("Beta ISO", selected!.Name);
            Assert.Single(conversation.PendingActions);
            Assert.Equal("yes_no", conversation.PendingActions[0].ExpectedResponse.Type);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public void ResolveText_with_index_pending_returns_NotHandled_for_unrelated_text()
    {
        var store = new TorrentBot.Engine.Context.ConversationContextStore();
        var conversation = store.GetOrCreate("chat-1", "admin");
        var contract = new CapabilityContract(
            "torrent.search",
            "search",
            [],
            RiskLevel.Safe);
        conversation.AddPendingAction(new PendingUserAction(
            "token-1",
            "torrent.select_result",
            contract,
            new ExpectedResponseShape("index", "index")));

        var handler = new ConversationResponseHandler(store);
        var resolution = handler.Resolve("chat-1", "admin", callbackData: null, text: "szukaj debian");
        Assert.False(resolution.Handled);
    }

    [Fact]
    public async Task ProcessUserResponseAsync_cancel_does_not_execute_capability()
    {
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var services = PipelineBootstrap.Create(engine, engine.LlmPipeline);
            var conversation = engine.ConversationContextStore!.GetOrCreate("chat-1", "admin");
            var contract = new CapabilityContract(
                "torrent.delete",
                "delete torrent",
                [],
                RiskLevel.Destructive,
                UserInteractions: new UserInteractionSpec(ExpectedResponseTypes: ["yes_no"]));

            var token = "pending-delete";
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

    private static DownloadSearchResult? ExtractSelected(ExecutionResult result)
    {
        if (result.CapabilityResult?.Data is not Dictionary<string, object?> data
            || !data.TryGetValue("selected", out var selected))
        {
            return null;
        }

        return selected as DownloadSearchResult;
    }
}