using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine.Conversation;

namespace TorrentBot.Bootstrap;

public sealed record PipelineServices(
    IInvocationPipeline Invocation,
    IConversationPipeline Conversation);