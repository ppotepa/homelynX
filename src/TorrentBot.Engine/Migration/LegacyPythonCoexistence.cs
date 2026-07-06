namespace TorrentBot.Engine.Migration;

public sealed class LegacyPythonCoexistence
{
    public bool ShouldRouteToNewEngine(FeatureFlags flags, string? source = null) =>
        flags.EnableNewEnginePath && !flags.EnableLegacyPythonShim;

    public bool ShouldDelegateToLegacyPython(FeatureFlags flags, string? source = null) =>
        flags.EnableLegacyPythonShim && !flags.EnableNewEnginePath;

    public CoexistenceDecision Resolve(FeatureFlags flags, string source)
    {
        if (ShouldDelegateToLegacyPython(flags, source))
        {
            return new CoexistenceDecision(false, true, "Routing to legacy Python handler.");
        }

        if (ShouldRouteToNewEngine(flags, source))
        {
            return new CoexistenceDecision(true, false, "Routing to C# Engine.");
        }

        return new CoexistenceDecision(true, false, "Defaulting to C# Engine while legacy shim remains available.");
    }
}

public sealed record CoexistenceDecision(bool UseNewEngine, bool UseLegacyPython, string Reason);