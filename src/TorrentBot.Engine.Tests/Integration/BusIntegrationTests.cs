using TorrentBot.Contracts.Bus;
using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class BusIntegrationTests
{
    [Fact]
    public async Task Capability_execution_publishes_bus_message_to_engine_subscriber()
    {
        await using var scope = await EngineTestHelper.CreateStartedEngineAsync();
        var received = new TaskCompletionSource<CorrelatedMessage<TestBusMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var _ = scope.Engine.Subscribe<TestBusMessage>(message => received.TrySetResult(message));

        var result = await scope.Engine.SubmitAsync(EngineTestHelper.CreateInvocation(
            "test.publish",
            parameters: new Dictionary<string, object?> { ["value"] = "bus-payload" }));

        Assert.True(result.Success, "test.publish capability must execute through orchestrator registry");

        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("bus-payload", message.Payload.Value);
        Assert.Equal("trace-123", message.Context.TraceId);
        Assert.Equal("user-789", message.Context.UserId);
    }
}