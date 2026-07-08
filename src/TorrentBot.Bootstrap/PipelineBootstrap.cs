using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine;
using TorrentBot.Engine.Context;
using TorrentBot.Engine.Conversation;
using TorrentBot.Engine.Pipeline;
using TorrentBot.Engine.Pipeline.Behaviors;
using TorrentBot.Llm;

namespace TorrentBot.Bootstrap;

public static class PipelineBootstrap
{
    public static PipelineServices Create(EngineHost engine, LlmPipeline? llmPipeline = null)
    {
        var deterministic = new DeterministicPlanner(engine.ResolveCapabilityName);
        var conversationStore = engine.ConversationContextStore;
        var bus = engine.GetInternalBus();
        var constructor = new ContractResponseConstructor();

        IPlanner? llm = null;
        if (llmPipeline is not null)
        {
            llm = new LlmPlannerAdapter(
                llmPipeline.Planner,
                (user, scope) => engine.FilterCapabilitiesForUser(user, scope),
                () => engine.GetQuerySourceManifests(),
                conversationStore,
                () => engine.GetCapabilityContracts());
        }

        IConversationPipeline? conversationPipeline = null;
        var behaviors = new IPipelineBehavior[]
        {
            new ToolKnowledgeBehavior(),
            new ConversationStateBehavior(),
            new ResponseConstructionBehavior(constructor, bus),
            new ConversationPendingBehavior(() => conversationPipeline!),
            new PerTurnPromptBehavior()
        };

        var invocation = new InvocationPipeline(
            engine,
            deterministic,
            llm,
            behaviors,
            conversationStore,
            () => engine.GetCapabilityContracts(),
            bus);

        conversationPipeline = new ConversationPipeline(
            engine,
            invocation,
            engine.GetCapabilityRegistry(),
            bus!);

        return new PipelineServices(invocation, conversationPipeline);
    }
}