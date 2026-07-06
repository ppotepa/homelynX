using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;

namespace TorrentBot.Engine.Pipeline;

public sealed class DeterministicPlanner : IPlanner
{
    private readonly Func<Invocation, string?> _resolveCapability;

    public DeterministicPlanner(Func<Invocation, string?> resolveCapability) =>
        _resolveCapability = resolveCapability;

    public Task<ExecutionPlan> PlanAsync(Invocation invocation, PlanningContext context, CancellationToken ct = default)
    {
        if (context.IsReplay
            && !string.IsNullOrWhiteSpace(context.ReplayCapabilityName))
        {
            return Task.FromResult(new ExecutionPlan(
                PlanSource.Replay,
                [new ExecutionPlanStep(context.ReplayCapabilityName, context.ReplayParameters)],
                "replay"));
        }

        var capability = _resolveCapability(invocation);
        if (capability is null)
        {
            return Task.FromResult(new ExecutionPlan(PlanSource.Deterministic, [], "unresolved"));
        }

        return Task.FromResult(new ExecutionPlan(
            PlanSource.Deterministic,
            [new ExecutionPlanStep(capability, invocation.Parameters)],
            invocation.Command ?? capability));
    }
}