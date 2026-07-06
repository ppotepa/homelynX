using System.Text.Json;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Llm;
using TorrentBot.Contracts.Audit;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Llm;

public sealed record LlmPipelineRequest(
    string Text,
    IReadOnlyList<CapabilityMetadata> Capabilities,
    IReadOnlyList<QuerySourceMeta>? QuerySourceManifests = null,
    bool IsDryRun = false,
    string? Scope = "media",
    IRequestContext? AuditContext = null);

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
        var scopedCapabilities = request.Capabilities
            .Where(c => string.Equals(c.Scope, request.Scope ?? "media", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Scope, "all", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var queryManifests = request.QuerySourceManifests ?? [];
        var plan = await _planner.PlanAsync(
            new LlmPlanningRequest(
                request.Text,
                scopedCapabilities,
                queryManifests,
                request.Scope),
            ct).ConfigureAwait(false);

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

        var execution = _executor.Execute(new LlmExecutionRequest(plan, scopedCapabilities, request.IsDryRun));
        var reply = _responder.Compose(request.Text, plan, execution);

        if (_auditSink is not null && request.AuditContext is not null)
        {
            _auditSink.Write(
                "natural_response",
                request.AuditContext,
                "llm",
                execution.Success,
                JsonSerializer.Serialize(new { reply, success = execution.Success }));
        }

        return new LlmPipelineResult(plan, execution, reply);
    }
}