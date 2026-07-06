namespace TorrentBot.Contracts.Context;

public sealed class ExecutionContext : CapabilityContext
{
    public string? ParentJobId { get; init; }
    public IReadOnlyDictionary<string, object?> StepParams { get; init; } = new Dictionary<string, object?>();
}