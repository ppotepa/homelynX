namespace TorrentBot.Contracts.Capabilities;

public sealed record CapabilityResult(
    bool Success,
    object? Data = null,
    string? Message = null,
    string? JobId = null,
    bool IsDryRun = false);