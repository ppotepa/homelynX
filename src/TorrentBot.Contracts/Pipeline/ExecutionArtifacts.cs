using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Invocation;

namespace TorrentBot.Contracts.Pipeline;

public sealed record ExecutionArtifacts(
    bool Success,
    IReadOnlyList<IExecutionArtifact> Items,
    ExecutionResult? RawResult = null,
    string? Error = null);