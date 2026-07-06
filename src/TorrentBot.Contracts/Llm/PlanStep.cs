namespace TorrentBot.Contracts.Llm;

public sealed record PlanStep(
    string Capability,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    string? Why = null,
    bool ParallelSafe = false,
    string? ConfirmationToken = null,
    string? Condition = null,
    string? SaveAs = null);