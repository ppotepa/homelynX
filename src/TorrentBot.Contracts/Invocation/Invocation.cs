using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Pipeline;

namespace TorrentBot.Contracts.Invocation;

public sealed class Invocation
{
    public bool IsExplicit { get; init; }
    public string? CapabilityName { get; init; }
    public string? Command { get; init; }
    public string? Text { get; init; }
    public IReadOnlyDictionary<string, object?>? Parameters { get; init; }
    public required IRequestContext RequestContext { get; init; }
    public required UserContext User { get; init; }
    public bool IsDryRun { get; init; }
    public string? Condition { get; init; }
    public IProgressReporter? ProgressReporter { get; init; }
}