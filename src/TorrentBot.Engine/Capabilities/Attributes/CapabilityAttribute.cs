using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Engine.Capabilities.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CapabilityAttribute : Attribute
{
    public required string Name { get; init; }
    public string? Command { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Permission { get; init; } = "USER";
    public RiskLevel Risk { get; init; } = RiskLevel.Safe;
    public string? LlmUsage { get; init; }
    public string[] IntentHints { get; init; } = [];
    public string[] Preconditions { get; init; } = [];
    public bool IsLongRunning { get; init; }
    public bool IsReadOnly { get; init; }
}