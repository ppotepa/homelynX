using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine;
using TorrentBot.Engine.Context;
using TorrentBot.Engine.Conversation;
using TorrentBot.Engine.Pipeline;
using TorrentBot.Engine.Pipeline.Behaviors;

namespace TorrentBot.Bootstrap;

public static class PipelineBootstrap
{
    public static PipelineServices Create(EngineHost engine)
    {
        var deterministic = new DeterministicPlanner(engine.ResolveCapabilityName);
        var conversationStore = engine.ConversationContextStore;
        var bus = engine.GetInternalBus();
        var constructor = new ContractResponseConstructor();

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
