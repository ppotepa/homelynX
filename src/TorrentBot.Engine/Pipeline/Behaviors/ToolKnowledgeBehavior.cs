using TorrentBot.Contracts.Pipeline;

namespace TorrentBot.Engine.Pipeline.Behaviors;

public sealed class ToolKnowledgeBehavior : IPipelineBehavior
{
    public string Name => "tool_knowledge";

    public Task<PipelineBehaviorContext> BeforePlanAsync(PipelineBehaviorContext context, CancellationToken ct = default) =>
        Task.FromResult(context.WithState("contracts_count", context.Contracts.Count));

    public Task<(PipelineBehaviorContext Context, PipelineResult? UpdatedResult)> AfterExecutionAsync(
        PipelineBehaviorContext context,
        PipelineResult result,
        CancellationToken ct = default) =>
        Task.FromResult((context, (PipelineResult?)null));
}