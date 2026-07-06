namespace TorrentBot.Contracts.Pipeline;

public sealed record ExecutionPlanStep(
    string CapabilityName,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    string? SaveAs = null);