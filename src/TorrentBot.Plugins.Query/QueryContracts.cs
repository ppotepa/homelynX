using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Query;

internal static class QueryContracts
{
    public static readonly CapabilityContract Execute = new(
        Name: "query.execute",
        ExactSemantics: "Execute a safe structured query against a registered snapshot source.",
        Parameters:
        [
            new ParameterSpec("source", "string", "Registered query source name", Required: true),
            new ParameterSpec("where", "array", "Filter clauses"),
            new ParameterSpec("select", "array", "Field names"),
            new ParameterSpec("limit", "int", "Max rows")
        ],
        Risk: RiskLevel.Safe,
        ResponseSpec: new ResponseConstructionSpec("list", FormatHint: "query_results"),
        LlmUsage: "Use to inspect downloads, jobs, or runtime state. Not for content search.",
        IntentHints: ["query", "list", "show", "find"],
        IsReadOnly: true);
}