namespace TorrentBot.Contracts.Capabilities;

public sealed record CapabilityMetadata(
    string Name,
    string? Command,
    string Description,
    string Permission,
    RiskLevel Risk,
    string? LlmUsage = null,
    IReadOnlyList<string>? IntentHints = null,
    IReadOnlyList<string>? Preconditions = null,
    bool IsLongRunning = false,
    bool IsReadOnly = false,
    string Scope = "media");