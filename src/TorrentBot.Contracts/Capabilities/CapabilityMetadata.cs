namespace TorrentBot.Contracts.Capabilities;

public sealed record CapabilityMetadata(
    string Name,
    string? Command,
    string Description,
    string Permission,
    RiskLevel Risk,
    IReadOnlyList<string>? Preconditions = null,
    bool IsLongRunning = false,
    bool IsReadOnly = false,
    string Scope = "media");
