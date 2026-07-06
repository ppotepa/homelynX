using TorrentBot.Contracts.Context;

namespace TorrentBot.Contracts.Pipeline;

public interface IPlanner
{
    Task<ExecutionPlan> PlanAsync(Invocation.Invocation invocation, PlanningContext context, CancellationToken ct = default);
}

public sealed record PlanningContext(
    UserContext User,
    bool IsReplay,
    string? ReplayCapabilityName = null,
    IReadOnlyDictionary<string, object?>? ReplayParameters = null);