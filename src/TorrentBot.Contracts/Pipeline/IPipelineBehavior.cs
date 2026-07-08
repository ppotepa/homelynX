using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Contracts.Pipeline;

public interface IPipelineBehavior
{
    string Name { get; }
    Task<PipelineBehaviorContext> BeforePlanAsync(PipelineBehaviorContext context, CancellationToken ct = default);
    Task<(PipelineBehaviorContext Context, PipelineResult? UpdatedResult)> AfterExecutionAsync(
        PipelineBehaviorContext context,
        PipelineResult result,
        CancellationToken ct = default);
}

public sealed record PipelineBehaviorContext(
    TorrentBot.Contracts.Invocation.Invocation Invocation,
    ConversationContext? Conversation,
    IReadOnlyList<CapabilityContract> Contracts,
    IReadOnlyList<CapabilityMetadata> Capabilities,
    IReadOnlyDictionary<string, object?> State)
{
    public PipelineBehaviorContext WithState(string key, object? value)
    {
        var next = new Dictionary<string, object?>(State, StringComparer.Ordinal) { [key] = value };
        return this with { State = next };
    }
}