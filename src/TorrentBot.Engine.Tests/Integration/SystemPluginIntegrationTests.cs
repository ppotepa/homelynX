using TorrentBot.Engine.Tests.Support;
using TorrentBot.Plugins.System;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class SystemPluginIntegrationTests
{
    [Fact]
    public async Task System_health_capability_returns_healthy_status()
    {
        IEngine engine = new EngineHost();
        engine.RegisterPlugin(new SystemPlugin());
        await engine.StartAsync();

        var result = await engine.SubmitAsync(EngineTestHelper.CreateInvocation("system.health"));

        Assert.True(result.Success);
        var data = Assert.IsType<Dictionary<string, object?>>(result.CapabilityResult!.Data);
        Assert.Equal("healthy", data["status"]);
        Assert.Equal("running", data["engine"]);

        await engine.StopAsync();
    }

    [Fact]
    public async Task System_status_reports_jobs_and_query_sources()
    {
        IEngine engine = new EngineHost();
        engine.RegisterPlugin(new SystemPlugin());
        await engine.StartAsync();

        var result = await engine.SubmitAsync(EngineTestHelper.CreateInvocation("system.status"));

        Assert.True(result.Success);
        var data = Assert.IsType<Dictionary<string, object?>>(result.CapabilityResult!.Data);
        Assert.Equal(0, data["totalJobs"]);
        var sources = Assert.IsAssignableFrom<string[]>(data["querySources"]);
        Assert.Contains("system.runtime", sources);

        await engine.StopAsync();
    }

    [Fact]
    public async Task System_capabilities_lists_registered_system_capabilities()
    {
        IEngine engine = new EngineHost();
        engine.RegisterPlugin(new SystemPlugin());
        await engine.StartAsync();

        var result = await engine.SubmitAsync(EngineTestHelper.CreateInvocation("system.capabilities"));

        Assert.True(result.Success);
        var data = Assert.IsType<Dictionary<string, object?>>(result.CapabilityResult!.Data);
        Assert.Equal(8, data["count"]);
        var list = Assert.IsAssignableFrom<List<Dictionary<string, object?>>>(data["capabilities"]);
        Assert.Contains(list, c => (string?)c["name"] == "system.health");

        await engine.StopAsync();
    }
}