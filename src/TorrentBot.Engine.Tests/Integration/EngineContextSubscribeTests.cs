using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class EngineContextSubscribeTests
{
    [Fact]
    public async Task Capability_uses_IEngineContext_Subscribe_to_receive_published_message()
    {
        await using var scope = await EngineTestHelper.CreateStartedEngineAsync();

        var result = await scope.Engine.SubmitAsync(EngineTestHelper.CreateInvocation(
            "test.context_subscribe",
            parameters: new Dictionary<string, object?> { ["value"] = "narrow-surface-payload" }));

        Assert.True(result.Success, "test.context_subscribe must succeed via deterministic orchestrator path");
        var data = Assert.IsType<Dictionary<string, object?>>(result.CapabilityResult!.Data);
        Assert.Equal("narrow-surface-payload", data["received"]);
        Assert.Equal("narrow-surface-payload", data["expected"]);
    }
}