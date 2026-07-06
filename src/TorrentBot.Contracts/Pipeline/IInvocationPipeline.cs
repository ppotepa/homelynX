namespace TorrentBot.Contracts.Pipeline;

public interface IInvocationPipeline
{
    Task<PipelineResult> RunAsync(Invocation.Invocation invocation, CancellationToken ct = default);
}

public sealed record PipelineResult(
    bool Success,
    ExecutionArtifacts Artifacts,
    ExecutionPlan Plan,
    string? Error = null);