using TorrentBot.Engine;
using TorrentBot.Engine.Conversation;
using TorrentBot.Engine.Pipeline;

namespace TorrentBot.Bootstrap;

public static class PipelineBootstrap
{
    public static PipelineServices Create(EngineHost engine)
    {
        var constructor = new ContractResponseConstructor();
        IConversationPipeline? conversationPipeline = null;

        var invocation = new InvocationPipeline(
            engine,
            engine.ConversationContextStore,
            () => engine.GetCapabilityContracts(),
            constructor,
            () => conversationPipeline);

        conversationPipeline = new ConversationPipeline(
            engine,
            invocation,
            engine.GetCapabilityRegistry(),
            engine.GetInternalBus()!);

        return new PipelineServices(invocation, conversationPipeline);
    }
}
