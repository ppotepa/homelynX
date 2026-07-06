namespace TorrentBot.Engine;

public sealed class FeatureFlags
{
    public bool EnableNewEnginePath { get; init; } = true;
    public bool EnableLegacyPythonShim { get; init; }

    public static FeatureFlags FromEnvironment() => new()
    {
        EnableNewEnginePath = ReadBool("TORRENTBOT_ENABLE_NEW_ENGINE", defaultValue: true),
        EnableLegacyPythonShim = ReadBool("TORRENTBOT_ENABLE_LEGACY_PYTHON", defaultValue: false)
    };

    private static bool ReadBool(string name, bool defaultValue) =>
        Environment.GetEnvironmentVariable(name) switch
        {
            null => defaultValue,
            var value when bool.TryParse(value, out var parsed) => parsed,
            var value => value is "1" or "yes" or "on"
        };
}