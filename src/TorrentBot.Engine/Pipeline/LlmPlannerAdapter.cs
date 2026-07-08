using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Repositories;
using TorrentBot.Engine.Context;
using TorrentBot.Llm;

namespace TorrentBot.Engine.Pipeline;

public sealed class LlmPlannerAdapter : IPlanner
{
    private readonly ILlmPlanner _planner;
    private readonly Func<UserContext, string?, IReadOnlyList<CapabilityMetadata>> _filterCapabilities;
    private readonly Func<IReadOnlyList<QuerySourceMeta>> _querySources;
    private readonly ConversationContextStore? _conversationStore;
    private readonly Func<IReadOnlyList<CapabilityContract>>? _contractsProvider;

    public LlmPlannerAdapter(
        ILlmPlanner planner,
        Func<UserContext, string?, IReadOnlyList<CapabilityMetadata>> filterCapabilities,
        Func<IReadOnlyList<QuerySourceMeta>> querySources,
        ConversationContextStore? conversationStore = null,
        Func<IReadOnlyList<CapabilityContract>>? contractsProvider = null)
    {
        _planner = planner;
        _filterCapabilities = filterCapabilities;
        _querySources = querySources;
        _conversationStore = conversationStore;
        _contractsProvider = contractsProvider;
    }

    public async Task<ExecutionPlan> PlanAsync(Invocation invocation, PlanningContext context, CancellationToken ct = default)
    {
        var scope = invocation.RequestContext.Properties?.TryGetValue("scope", out var scopeValue) == true
            ? scopeValue?.ToString() ?? "media"
            : "media";

        var allowed = _filterCapabilities(invocation.User, scope);
        var sessionId = invocation.RequestContext?.ChatId ?? invocation.RequestContext?.TraceId ?? "default";
        var conversation = _conversationStore?.GetOrCreate(sessionId, invocation.User.UserId);
        var requestNumber = conversation?.NextRequestNumber() ?? 0;
        if (conversation is not null && !string.IsNullOrWhiteSpace(invocation.Text))
        {
            conversation.AddMessage("user", invocation.Text, requestNumber);
        }

        var plan = await _planner.PlanAsync(
            new LlmPlanningRequest(
                invocation.Text ?? string.Empty,
                allowed,
                _querySources(),
                scope,
                conversation,
                requestNumber,
                invocation.ProgressReporter,
                _contractsProvider?.Invoke()),
            ct).ConfigureAwait(false);

        var steps = plan.Steps
            .Select(step => new ExecutionPlanStep(step.Capability, step.Parameters, step.SaveAs, step.Condition))
            .ToList();

        return new ExecutionPlan(PlanSource.Llm, steps, plan.Intent);
    }
}