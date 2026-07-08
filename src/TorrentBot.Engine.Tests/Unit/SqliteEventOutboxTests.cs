using TorrentBot.Contracts.Bus.Events;
using TorrentBot.Engine.Bus;
using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class SqliteEventOutboxTests
{
    [Fact]
    public async Task QueuedEventBus_persists_published_events_to_sqlite_outbox()
    {
        using var outbox = SqliteEventOutbox.CreateInMemory();
        await using var bus = new QueuedEventBus(outbox: outbox);
        var ctx = EngineTestHelper.CreateRequestContext("trace-outbox", "inv-outbox", "user-outbox");

        bus.Publish(new ConversationStateChangedEvent("chat-1", "admin", 1, "pending_added"), ctx);

        await Task.Delay(100);

        var entries = outbox.ReadRecent(10);
        Assert.Contains(entries, e =>
            e.EventType == nameof(ConversationStateChangedEvent)
            && e.TraceId == "trace-outbox"
            && e.PayloadJson.Contains("pending_added", StringComparison.Ordinal));
    }
}