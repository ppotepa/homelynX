using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Plugins;

namespace TorrentBot.Plugins.Query;

public sealed class QueryPlugin : IPlugin
{
    public string Name => "query";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        context.RegisterCapability(
            new CapabilityMetadata(
                Name: "query.execute",
                Command: null,
                Description: "Execute a safe structured query against a registered snapshot source",
                Permission: "USER",
                Risk: RiskLevel.Safe,
                LlmUsage: "Use to inspect downloads, jobs, media, or runtime state",
                IntentHints: ["query", "list", "show", "find", "how many"],
                IsReadOnly: true),
            new QueryExecuteHandler());
    }
}