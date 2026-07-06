using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class DeterministicInvocationTests
{
    [Fact]
    public async Task SubmitAsync_executes_registered_capability_and_returns_success()
    {
        await using var engineScope = await EngineTestHelper.CreateStartedEngineAsync();

        var result = await engineScope.Engine.SubmitAsync(EngineTestHelper.CreateInvocation(
            "test.echo",
            parameters: new Dictionary<string, object?> { ["message"] = "hello-engine" }));

        Assert.True(result.Success, "orchestrator must start, register test plugin, and execute explicit test.echo");
        Assert.NotNull(result.CapabilityResult);
        Assert.False(string.IsNullOrWhiteSpace(result.CapabilityResult!.Message));
        Assert.Equal("hello-engine", result.CapabilityResult.Message);
    }
}