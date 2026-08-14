using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.System.Capabilities;

internal static class SystemContracts
{
    public static readonly CapabilityContract Health = new(
        Name: "system.health",
        ExactSemantics: "Returns basic engine health status.",
        Parameters: [],
        Risk: RiskLevel.Safe,
        ResponseSpec: new ResponseConstructionSpec("text"),
        LlmUsage: "Use when user asks if the bot is alive.",
        IntentHints: ["health", "ping", "alive"],
        IsReadOnly: true,
        Scope: "all");

    public static readonly CapabilityContract Status = new(
        Name: "system.status",
        ExactSemantics: "Returns engine runtime status and loaded plugins.",
        Parameters: [],
        Risk: RiskLevel.Safe,
        ResponseSpec: new ResponseConstructionSpec("text"),
        IsReadOnly: true,
        Scope: "all");

    public static readonly CapabilityContract Capabilities = new(
        Name: "system.capabilities",
        ExactSemantics: "Lists capabilities available to the current user with optional filter.",
        Parameters: [new ParameterSpec("filter", "string", "Narrow by domain e.g. download, torrent")],
        Risk: RiskLevel.Safe,
        ResponseSpec: new ResponseConstructionSpec("list"),
        LlmUsage: "Use when user asks what commands exist. Pass filter for scoped lists.",
        IntentHints: ["capabilities", "commands", "komendy"],
        IsReadOnly: true,
        Scope: "all");

    public static readonly CapabilityContract Help = new(
        Name: "system.help",
        ExactSemantics: "Show available commands for the current user with optional filter.",
        Parameters: [new ParameterSpec("filter", "string", "Narrow by domain e.g. download, torrent")],
        Risk: RiskLevel.Safe,
        ResponseSpec: new ResponseConstructionSpec("list"),
        LlmUsage: "Use when user asks what commands exist or how to use the bot.",
        IntentHints: ["help", "commands", "jakie komendy"],
        IsReadOnly: true,
        Scope: "all");

    public static readonly CapabilityContract Ping = new(
        Name: "bot.ping",
        ExactSemantics: "Responds with pong.",
        Parameters: [],
        Risk: RiskLevel.Safe,
        IsReadOnly: true,
        Scope: "all");

    public static readonly CapabilityContract DiskUsage = new(
        Name: "system.disk_usage",
        ExactSemantics: "Show disk usage for the media root drive. Only for storage questions.",
        Parameters: [],
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityContract FindLargeFiles = new(
        Name: "system.find_large_files",
        ExactSemantics: "Find large files under media root for cleanup.",
        Parameters: [],
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

}
