using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Repositories;
using TorrentBot.Llm;

namespace TorrentBot.Engine.Pipeline;

public sealed class LlmPlannerAdapter : IPlanner
{
    private readonly ILlmPlanner _planner;
    private readonly Func<UserContext, string?, IReadOnlyList<CapabilityMetadata>> _filterCapabilities;
    private readonly Func<IReadOnlyList<QuerySourceMeta>> _querySources;

    public LlmPlannerAdapter(
        ILlmPlanner planner,
        Func<UserContext, string?, IReadOnlyList<CapabilityMetadata>> filterCapabilities,
        Func<IReadOnlyList<QuerySourceMeta>> querySources)
    {
        _planner = planner;
        _filterCapabilities = filterCapabilities;
        _querySources = querySources;
    }

    public async Task<ExecutionPlan> PlanAsync(Invocation invocation, PlanningContext context, CancellationToken ct = default)
    {
        var scope = invocation.RequestContext.Properties?.TryGetValue("scope", out var scopeValue) == true
            ? scopeValue?.ToString() ?? "media"
            : "media";

        var allowed = _filterCapabilities(invocation.User, scope);
        var plan = await _planner.PlanAsync(
            new LlmPlanningRequest(
                invocation.Text ?? string.Empty,
                allowed,
                _querySources(),
                scope),
            ct).ConfigureAwait(false);

        var steps = plan.Steps
            .Select(step => new ExecutionPlanStep(step.Capability, step.Parameters, step.SaveAs))
            .ToList();

        return new ExecutionPlan(PlanSource.Llm, steps, plan.Intent);
    }
}