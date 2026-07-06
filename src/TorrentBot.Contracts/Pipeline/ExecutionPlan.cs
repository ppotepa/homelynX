namespace TorrentBot.Contracts.Pipeline;

public sealed record ExecutionPlan(
    PlanSource Source,
    IReadOnlyList<ExecutionPlanStep> Steps,
    string? Intent = null);