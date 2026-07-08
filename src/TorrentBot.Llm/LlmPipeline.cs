using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Llm;
using TorrentBot.Contracts.Audit;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Repositories;
namespace TorrentBot.Llm;

public sealed record LlmPipelineRequest(
    string Text,
    IReadOnlyList<CapabilityMetadata> Capabilities,
    IReadOnlyList<QuerySourceMeta>? QuerySourceManifests = null,
    bool IsDryRun = false,
    string? Scope = "media",
    IRequestContext? AuditContext = null,
    ConversationContext? Conversation = null,
    int RequestNumber = 0,
    IProgressReporter? ProgressReporter = null,
    IReadOnlyList<CapabilityContract>? Contracts = null);

public sealed record LlmPipelineResult(
    PlanEnvelope Plan,
    LlmExecutionResult Execution,
    string Reply);

public sealed class LlmPipeline
{
    private readonly ILlmPlanner _planner;

    public ILlmPlanner Planner => _planner;
    private readonly ILlmExecutor _executor;
    private readonly ILlmResponder _responder;
    private readonly IAuditSink? _auditSink;

    public LlmPipeline(
        ILlmPlanner planner,
        ILlmExecutor executor,
        ILlmResponder? responder = null,
        IAuditSink? auditSink = null)
    {
        _planner = planner;
        _executor = executor;
        _responder = responder ?? new DeterministicLlmResponder();
        _auditSink = auditSink;
    }

    public async Task<LlmPipelineResult> RunAsync(LlmPipelineRequest request, CancellationToken ct = default)
    {
        var intent = LlmIntentNormalizer.Analyze(request.Text ?? string.Empty);
        if (request.ProgressReporter is not null && intent.WasNormalized)
        {
            request.ProgressReporter.Report(
                "debug:llm:normalized",
                $"\"{intent.OriginalText}\" → \"{intent.NormalizedText}\"");
        }

        var scopedCapabilities = request.Capabilities
            .Where(c => string.Equals(c.Scope, request.Scope ?? "media", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Scope, "all", StringComparison.OrdinalIgnoreCase))
            .ToList();

        request.ProgressReporter?.Report("debug:phase:start", "llm_prompt_build");
        request.ProgressReporter?.Report("debug:llm:start", $"normalized_len={intent.NormalizedText.Length} caps={scopedCapabilities.Count}");

        var queryManifests = request.QuerySourceManifests ?? [];
        request.ProgressReporter?.Report("debug:phase:end", "llm_prompt_build");
        request.ProgressReporter?.Report("debug:phase:start", "llm_call");
        var plan = await _planner.PlanAsync(
            new LlmPlanningRequest(
                intent.NormalizedText,
                scopedCapabilities,
                queryManifests,
                request.Scope,
                request.Conversation,
                request.RequestNumber,
                request.ProgressReporter,
                request.Contracts),
            ct).ConfigureAwait(false);

        plan = LlmPlanRepairer.Repair(
            intent,
            plan,
            request.Conversation,
            request.RequestNumber,
            request.ProgressReporter);

        if (_auditSink is not null && request.AuditContext is not null)
        {
            _auditSink.Write(
                "natural_plan",
                request.AuditContext,
                "llm",
                plan.Steps.Count > 0,
                JsonSerializer.Serialize(new { intent = plan.Intent, steps = plan.Steps.Count, scope = request.Scope }));
        }

        if (_executor is AuditingLlmExecutor auditing && request.AuditContext is not null)
        {
            auditing.SetAuditContext(request.AuditContext);
        }

        request.ProgressReporter?.Report("debug:phase:end", "llm_call");
        request.ProgressReporter?.Report("debug:phase:start", "execution");
        var execution = await _executor.Execute(new LlmExecutionRequest(plan, scopedCapabilities, request.IsDryRun)).ConfigureAwait(false);

        if (_auditSink is not null && request.AuditContext is not null)
        {
            _auditSink.Write(
                "natural_execution",
                request.AuditContext,
                "llm",
                execution.Success,
                JsonSerializer.Serialize(new { success = execution.Success, stepsToExecute = execution.StepsToExecute.Count }));
        }

        request.ProgressReporter?.Report("debug:phase:end", "execution");
        return new LlmPipelineResult(plan, execution, string.Empty);
    }

    public async Task<string> ComposeReply(string userText, PlanEnvelope plan, LlmExecutionResult execution, CapabilityResult? lastResult = null)
    {
        var reply = await _responder.Compose(userText, plan, execution, lastResult).ConfigureAwait(false);

        if (_auditSink is not null && execution.StepsToExecute.Count > 0)
        {
            _auditSink.Write(
                "natural_response",
                new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "system", source: "llm"),
                "llm",
                execution.Success,
                JsonSerializer.Serialize(new { reply, success = execution.Success }));
        }

        return reply;
    }
}