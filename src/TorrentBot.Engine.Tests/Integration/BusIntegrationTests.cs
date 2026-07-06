using TorrentBot.Contracts.Bus;
using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class BusIntegrationTests
{
    [Fact]
    public async Task Capability_execution_publishes_bus_message_to_engine_subscriber()
    {
        await using var scope = await EngineTestHelper.CreateStartedEngineAsync();
        CorrelatedMessage<TestBusMessage>? received = null;

        using var _ = scope.Engine.Subscribe<TestBusMessage>(message => received = message);

        var result = await scope.Engine.SubmitAsync(EngineTestHelper.CreateInvocation(
            "test.publish",
            parameters: new Dictionary<string, object?> { ["value"] = "bus-payload" }));

        Assert.True(result.Success, "test.publish capability must execute through orchestrator registry");
        Assert.NotNull(received);
        Assert.Equal("bus-payload", received!.Payload.Value);
        Assert.Equal("trace-123", received.Context.TraceId);
        Assert.Equal("user-789", received.Context.UserId);
    }
}