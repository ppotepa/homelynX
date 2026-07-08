using TorrentBot.Contracts.Bus.Events;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Conversation;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine.Bus;
using TorrentBot.Engine.Context;
using TorrentBot.Llm;

namespace TorrentBot.Engine.Pipeline;

public sealed class InvocationPipeline : IInvocationPipeline
{
    private readonly IEngine _engine;
    private readonly IPlanner _deterministicPlanner;
    private readonly IPlanner? _llmPlanner;
    private readonly IReadOnlyList<IPipelineBehavior> _behaviors;
    private readonly ConversationContextStore? _conversationStore;
    private readonly Func<IReadOnlyList<CapabilityContract>>? _contractsProvider;
    private readonly IInternalBus? _bus;

    public InvocationPipeline(
        IEngine engine,
        IPlanner deterministicPlanner,
        IPlanner? llmPlanner = null,
        IEnumerable<IPipelineBehavior>? behaviors = null,
        ConversationContextStore? conversationStore = null,
        Func<IReadOnlyList<CapabilityContract>>? contractsProvider = null,
        IInternalBus? bus = null)
    {
        _engine = engine;
        _deterministicPlanner = deterministicPlanner;
        _llmPlanner = llmPlanner;
        _behaviors = behaviors?.ToList() ?? [];
        _conversationStore = conversationStore;
        _contractsProvider = contractsProvider;
        _bus = bus;
    }

    public async Task<PipelineResult> RunAsync(Invocation invocation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var progress = invocation.ProgressReporter;
        var sessionId = invocation.RequestContext?.ChatId ?? invocation.RequestContext?.TraceId ?? "default";
        var conversation = _conversationStore?.GetOrCreate(sessionId, invocation.User?.UserId ?? "unknown");
        var contracts = _contractsProvider?.Invoke() ?? [];
        var capabilities = _engine is EngineHost host
            ? host.FilterCapabilitiesForUser(invocation.User!, null)
            : [];

        var behaviorContext = new PipelineBehaviorContext(
            invocation,
            conversation,
            contracts,
            capabilities,
            new Dictionary<string, object?>(StringComparer.Ordinal));

        foreach (var behavior in _behaviors)
        {
            behaviorContext = await behavior.BeforePlanAsync(behaviorContext, ct).ConfigureAwait(false);
        }

        if (progress is not null)
        {
            var kind = invocation.IsExplicit ? "explicit" : "nl";
            var capOrText = invocation.IsExplicit ? invocation.CapabilityName ?? invocation.Command : _Truncate(invocation.Text, 60);
            progress.Report("debug:invocation:start", $"type={kind} user={invocation.User?.UserId} cap/text={capOrText} dry={invocation.IsDryRun}");
            progress.Report("debug:phase:start", "planning");
        }

        var planningContext = new PlanningContext(invocation.User, IsReplay(invocation), GetReplayCapability(invocation), invocation.Parameters);
        var planner = SelectPlanner(invocation, planningContext);

        progress?.Report("planning:start", planner == _llmPlanner ? "llm" : "deterministic");

        var plan = await planner.PlanAsync(invocation, planningContext, ct).ConfigureAwait(false);

        progress?.Report("planning:done", $"{plan.Steps.Count}|{plan.Intent}");
        progress?.Report("debug:phase:end", "planning");
        progress?.Report("debug:phase:start", "execution");

        if (plan.Steps.Count == 0)
        {
            var unresolved = new ExecutionResult(Success: false, Error: invocation.IsExplicit
                ? "Capability was not resolved."
                : plan.Source == PlanSource.Llm
                    ? "LLM could not derive a plan for that request. Try a slash command from /help."
                    : "I could not derive a plan for that request.");
            return await FinalizeAsync(behaviorContext, new PipelineResult(false, ArtifactAccumulator.FromExecutionResult(unresolved), plan, unresolved.Error), ct).ConfigureAwait(false);
        }

        ExecutionResult? last = null;
        var saved = new Dictionary<string, object?>(StringComparer.Ordinal);
        var totalSteps = plan.Steps.Count;
        var stepIndex = 0;
        foreach (var step in plan.Steps)
        {
            stepIndex++;
            if (!PlanStepConditionEvaluator.ShouldExecute(step.Condition, saved))
            {
                progress?.Report("step:skip", $"{stepIndex}/{totalSteps}|{step.CapabilityName}");
                continue;
            }

            progress?.Report("step:start", $"{stepIndex}/{totalSteps}|{step.CapabilityName}");

            if (invocation.RequestContext is not null)
            {
                _bus?.Publish(new ToolCallEvent(step.CapabilityName, step.Parameters), invocation.RequestContext);
            }

            var stepInvocation = new Invocation
            {
                IsExplicit = true,
                CapabilityName = step.CapabilityName,
                Parameters = step.Parameters,
                RequestContext = invocation.RequestContext,
                User = invocation.User,
                IsDryRun = invocation.IsDryRun,
                Condition = step.Condition,
                ProgressReporter = progress
            };

            behaviorContext = behaviorContext with { Invocation = stepInvocation };

            last = await _engine.SubmitAsync(stepInvocation, ct).ConfigureAwait(false);
            if (!last.Success)
            {
                progress?.Report("step:error", $"{stepIndex}/{totalSteps}|{step.CapabilityName}|{last.Error}");
                return await FinalizeAsync(
                    behaviorContext,
                    new PipelineResult(false, ArtifactAccumulator.FromExecutionResult(last), plan, last.Error),
                    ct).ConfigureAwait(false);
            }

            progress?.Report("step:done", $"{stepIndex}/{totalSteps}|{step.CapabilityName}|{ExtractStepDetail(last)}");

            if (!string.IsNullOrWhiteSpace(step.SaveAs))
            {
                saved[step.SaveAs] = last.CapabilityResult?.Data ?? new Dictionary<string, object?>();
            }
        }

        progress?.Report("debug:phase:end", "execution");
        progress?.Report("debug:pipeline:complete", $"steps_executed={totalSteps} success=true");
        return await FinalizeAsync(
            behaviorContext,
            new PipelineResult(true, ArtifactAccumulator.FromExecutionResult(last!), plan),
            ct).ConfigureAwait(false);
    }

    private async Task<PipelineResult> FinalizeAsync(
        PipelineBehaviorContext behaviorContext,
        PipelineResult pipelineResult,
        CancellationToken ct)
    {
        var current = pipelineResult;
        foreach (var behavior in _behaviors)
        {
            var (ctx, updated) = await behavior.AfterExecutionAsync(behaviorContext, current, ct).ConfigureAwait(false);
            behaviorContext = ctx;
            if (updated is not null)
            {
                current = updated;
            }
        }

        return current;
    }

    private static string ExtractStepDetail(ExecutionResult result)
    {
        if (result.CapabilityResult?.Data is not Dictionary<string, object?> data)
        {
            return "";
        }

        if (data.TryGetValue("count", out var count))
        {
            return $"count={count}";
        }

        if (data.TryGetValue("jobId", out var jobId))
        {
            return $"jobId={jobId}";
        }

        if (data.TryGetValue("confirmationRequired", out var cr) && cr is true)
        {
            return "confirmation_required";
        }

        return "";
    }

    private IPlanner SelectPlanner(Invocation invocation, PlanningContext context) =>
        context.IsReplay || invocation.IsExplicit ? _deterministicPlanner : _llmPlanner ?? _deterministicPlanner;

    private static bool IsReplay(Invocation invocation) =>
        invocation.Parameters?.ContainsKey("confirmationToken") == true;

    private static string? GetReplayCapability(Invocation invocation) =>
        invocation.CapabilityName ?? invocation.Command;

    private static string _Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text.Substring(0, max) + "…";
}