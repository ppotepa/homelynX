using TorrentBot.Contracts.Pipeline;

namespace TorrentBot.Engine.Pipeline.Behaviors;

public sealed class ConversationStateBehavior : IPipelineBehavior
{
    public string Name => "conversation_state";

    public Task<PipelineBehaviorContext> BeforePlanAsync(PipelineBehaviorContext context, CancellationToken ct = default)
    {
        if (context.Conversation is null)
        {
            return Task.FromResult(context);
        }

        return Task.FromResult(context.WithState("pending_actions", context.Conversation.PendingActions.Count));
    }

    public Task<(PipelineBehaviorContext Context, PipelineResult? UpdatedResult)> AfterExecutionAsync(
        PipelineBehaviorContext context,
        PipelineResult result,
        CancellationToken ct = default) =>
        Task.FromResult((context, (PipelineResult?)null));
}