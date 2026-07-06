using TorrentBot.Engine;
using TorrentBot.Engine.Migration;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class MigrationCoexistenceTests
{
    [Fact]
    public void Feature_flag_routes_main_media_to_new_engine_by_default()
    {
        var router = new LegacyPythonCoexistence();
        var decision = router.Resolve(new FeatureFlags { EnableNewEnginePath = true, EnableLegacyPythonShim = false }, "telegram");
        Assert.True(decision.UseNewEngine);
        Assert.False(decision.UseLegacyPython);
    }

    [Fact]
    public void Legacy_python_shim_can_be_enabled_without_disabling_new_engine()
    {
        var flags = FeatureFlags.FromEnvironment();
        Environment.SetEnvironmentVariable("TORRENTBOT_ENABLE_LEGACY_PYTHON", "true");
        Environment.SetEnvironmentVariable("TORRENTBOT_ENABLE_NEW_ENGINE", "true");
        var resolved = FeatureFlags.FromEnvironment();
        var router = new LegacyPythonCoexistence();
        var decision = router.Resolve(resolved, "cli");
        Assert.True(decision.UseNewEngine);
        Environment.SetEnvironmentVariable("TORRENTBOT_ENABLE_LEGACY_PYTHON", null);
        Environment.SetEnvironmentVariable("TORRENTBOT_ENABLE_NEW_ENGINE", null);
        _ = flags;
    }
}