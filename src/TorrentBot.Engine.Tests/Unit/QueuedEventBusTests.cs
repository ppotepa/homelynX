using TorrentBot.Contracts.Bus;
using TorrentBot.Engine.Bus;
using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class QueuedEventBusTests
{
    [Fact]
    public async Task Publish_delivers_correlated_payload_to_subscriber_via_queue()
    {
        await using var bus = new QueuedEventBus();
        var ctx = EngineTestHelper.CreateRequestContext("trace-bus", "inv-bus", "user-bus");
        var received = new TaskCompletionSource<CorrelatedMessage<TestBusMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var _ = bus.Subscribe<TestBusMessage>(message => received.TrySetResult(message));
        bus.Publish(new TestBusMessage("hello"), ctx);

        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("hello", result.Payload.Value);
        Assert.Equal("trace-bus", result.Context.TraceId);
        Assert.Equal("user-bus", result.Context.UserId);
    }
}