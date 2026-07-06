using TorrentBot.Contracts.Context;

namespace TorrentBot.Contracts.Audit;

public interface IAuditSink
{
    void Write(string action, IRequestContext context, string capabilityName, bool success, string? detail = null);
}