using System.Text.Json;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Llm;
using TorrentBot.Contracts.Audit;

namespace TorrentBot.Llm;

public sealed class AuditingLlmExecutor : ILlmExecutor
{
    private readonly ILlmExecutor _inner;
    private readonly IAuditSink? _auditSink;
    private IRequestContext? _context;

    public AuditingLlmExecutor(ILlmExecutor inner, IAuditSink? auditSink = null)
    {
        _inner = inner;
        _auditSink = auditSink;
    }

    public void SetAuditContext(IRequestContext context) => _context = context;

    public LlmExecutionResult Execute(LlmExecutionRequest request)
    {
        var result = _inner.Execute(request);
        if (_auditSink is not null && _context is not null)
        {
            foreach (var stepResult in result.StepResults.Where(s => !s.Skipped))
            {
                var detail = JsonSerializer.Serialize(new
                {
                    capability = stepResult.Step.Capability,
                    success = stepResult.Success,
                    error = stepResult.Error,
                    condition = stepResult.Step.Condition,
                    saveAs = stepResult.Step.SaveAs
                });
                _auditSink.Write("natural_step", _context, stepResult.Step.Capability, stepResult.Success, detail);
            }
        }

        return result;
    }
}