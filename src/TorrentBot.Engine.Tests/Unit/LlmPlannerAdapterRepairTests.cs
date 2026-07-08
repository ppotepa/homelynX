using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine.Pipeline;
using TorrentBot.Llm;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class LlmPlannerAdapterRepairTests
{
    [Fact]
    public async Task PlanAsync_does_not_force_torrent_search_when_llm_returns_empty()
    {
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var adapter = new LlmPlannerAdapter(
                new UnconfiguredLlmPlanner(),
                (user, scope) => engine.FilterCapabilitiesForUser(user, scope),
                () => engine.GetQuerySourceManifests(),
                engine.ConversationContextStore,
                () => engine.GetCapabilityContracts());

            var plan = await adapter.PlanAsync(
                new Invocation
                {
                    Text = "download ubuntu 22 iso",
                    User = new TorrentBot.Acl.AclService().ResolveUser("admin"),
                    RequestContext = new RequestContext("trace", "chat-1", "admin", source: "test", chatId: "chat-1")
                },
                new PlanningContext(new TorrentBot.Acl.AclService().ResolveUser("admin"), IsReplay: false));

            Assert.Empty(plan.Steps);
        }
        finally
        {
            await engine.StopAsync();
        }
    }
}