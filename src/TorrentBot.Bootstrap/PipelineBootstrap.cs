using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine;
using TorrentBot.Engine.Pipeline;
using TorrentBot.Llm;

namespace TorrentBot.Bootstrap;

public static class PipelineBootstrap
{
    public static IInvocationPipeline Create(EngineHost engine, LlmPipeline? llmPipeline = null)
    {
        var deterministic = new DeterministicPlanner(engine.ResolveCapabilityName);
        IPlanner? llm = null;
        if (llmPipeline is not null)
        {
            llm = new LlmPlannerAdapter(
                llmPipeline.Planner,
                (user, scope) => engine.FilterCapabilitiesForUser(user, scope),
                () => engine.GetQuerySourceManifests());
        }

        return new InvocationPipeline(engine, deterministic, llm);
    }
}