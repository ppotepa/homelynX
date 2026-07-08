using TorrentBot.Contracts.Pipeline;

namespace TorrentBot.Engine.Pipeline.Behaviors;

public sealed class PerTurnPromptBehavior : IPipelineBehavior
{
    public string Name => "per_turn_prompt";

    public Task<PipelineBehaviorContext> BeforePlanAsync(PipelineBehaviorContext context, CancellationToken ct = default)
    {
        if (context.Conversation is null)
        {
            return Task.FromResult(context);
        }

        return Task.FromResult(context.WithState("request_number", context.Conversation.RequestCount));
    }

    public Task<(PipelineBehaviorContext Context, PipelineResult? UpdatedResult)> AfterExecutionAsync(
        PipelineBehaviorContext context,
        PipelineResult result,
        CancellationToken ct = default) =>
        Task.FromResult((context, (PipelineResult?)null));
}