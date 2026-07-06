using TorrentBot.Contracts.Audit;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Engine.Audit;

public sealed record AuditRecord(
    DateTimeOffset Timestamp,
    string Action,
    string CapabilityName,
    string UserId,
    string TraceId,
    bool Success,
    string? Detail = null);

public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly List<AuditRecord> _records = [];

    public void Write(string action, IRequestContext context, string capabilityName, bool success, string? detail = null)
    {
        _records.Add(new AuditRecord(
            DateTimeOffset.UtcNow,
            action,
            capabilityName,
            context.UserId,
            context.TraceId,
            success,
            detail));
    }

    public IReadOnlyList<AuditRecord> Records => _records;
}