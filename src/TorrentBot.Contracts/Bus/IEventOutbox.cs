using TorrentBot.Contracts.Context;

namespace TorrentBot.Contracts.Bus;

public sealed record OutboxEntry(
    string EventType,
    string TraceId,
    string UserId,
    string PayloadJson,
    DateTimeOffset CreatedAt);

public interface IEventOutbox
{
    void Append(string eventType, IRequestContext context, string payloadJson);

    IReadOnlyList<OutboxEntry> ReadRecent(int limit = 100);
}