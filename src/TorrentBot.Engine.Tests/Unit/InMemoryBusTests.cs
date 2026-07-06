using TorrentBot.Contracts.Bus;
using TorrentBot.Engine.Bus;
using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class InMemoryBusTests
{
    [Fact]
    public void Publish_delivers_correlated_payload_to_subscriber()
    {
        var bus = new InMemoryBus();
        var ctx = EngineTestHelper.CreateRequestContext("trace-bus", "inv-bus", "user-bus");
        CorrelatedMessage<TestBusMessage>? received = null;

        using var _ = bus.Subscribe<TestBusMessage>(message => received = message);
        bus.Publish(new TestBusMessage("hello"), ctx);

        Assert.NotNull(received);
        Assert.Equal("hello", received!.Payload.Value);
        Assert.Equal("trace-bus", received.Context.TraceId);
        Assert.Equal("user-bus", received.Context.UserId);
    }
}