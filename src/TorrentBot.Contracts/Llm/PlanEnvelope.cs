namespace TorrentBot.Contracts.Llm;

public sealed record PlanEnvelope(
    string Intent,
    IReadOnlyList<PlanStep> Steps,
    double Confidence = 1.0,
    string ReplyMode = "deterministic",
    string? Notes = null);