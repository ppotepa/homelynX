using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class EngineLifecycleTests
{
    [Fact]
    public async Task Start_registers_plugins_and_Stop_shuts_down()
    {
        var engine = new EngineHost();
        engine.RegisterPlugin(new TestPlugin());

        Assert.False(engine.IsRunning);

        await engine.StartAsync();
        Assert.True(engine.IsRunning);

        await engine.StopAsync();
        Assert.False(engine.IsRunning);
    }
}