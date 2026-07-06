using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Contracts.Invocation;

public sealed record ExecutionResult(
    bool Success,
    CapabilityResult? CapabilityResult = null,
    string? Error = null,
    bool IsDryRun = false);