using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;

namespace TorrentBot.Engine.Pipeline;

public sealed class InvocationPipeline : IInvocationPipeline
{
    private readonly IEngine _engine;
    private readonly IPlanner _deterministicPlanner;
    private readonly IPlanner? _llmPlanner;

    public InvocationPipeline(IEngine engine, IPlanner deterministicPlanner, IPlanner? llmPlanner = null)
    {
        _engine = engine;
        _deterministicPlanner = deterministicPlanner;
        _llmPlanner = llmPlanner;
    }

    public async Task<PipelineResult> RunAsync(Invocation invocation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var planningContext = new PlanningContext(invocation.User, IsReplay(invocation), GetReplayCapability(invocation), invocation.Parameters);
        var plan = await SelectPlanner(invocation, planningContext).PlanAsync(invocation, planningContext, ct).ConfigureAwait(false);

        if (plan.Steps.Count == 0)
        {
            var unresolved = new ExecutionResult(Success: false, Error: invocation.IsExplicit
                ? "Capability was not resolved."
                : plan.Source == PlanSource.Llm
                    ? "LLM could not derive a plan for that request. Try a slash command from /help."
                    : "I could not derive a plan for that request.");
            return new PipelineResult(false, ArtifactAccumulator.FromExecutionResult(unresolved), plan, unresolved.Error);
        }

        ExecutionResult? last = null;
        foreach (var step in plan.Steps)
        {
            var stepInvocation = new Invocation
            {
                IsExplicit = true,
                CapabilityName = step.CapabilityName,
                Parameters = step.Parameters,
                RequestContext = invocation.RequestContext,
                User = invocation.User,
                IsDryRun = invocation.IsDryRun
            };

            last = await _engine.SubmitAsync(stepInvocation, ct).ConfigureAwait(false);
            if (!last.Success)
            {
                return new PipelineResult(false, ArtifactAccumulator.FromExecutionResult(last), plan, last.Error);
            }
        }

        return new PipelineResult(true, ArtifactAccumulator.FromExecutionResult(last!), plan);
    }

    private IPlanner SelectPlanner(Invocation invocation, PlanningContext context)
    {
        if (context.IsReplay || invocation.IsExplicit)
        {
            return _deterministicPlanner;
        }

        return _llmPlanner ?? _deterministicPlanner;
    }

    private static bool IsReplay(Invocation invocation) =>
        invocation.Parameters?.ContainsKey("confirmationToken") == true;

    private static string? GetReplayCapability(Invocation invocation) =>
        invocation.CapabilityName ?? invocation.Command;
}