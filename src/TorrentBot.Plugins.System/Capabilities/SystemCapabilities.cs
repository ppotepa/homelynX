using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.System.Capabilities;

internal static class SystemCapabilities
{
    public static readonly CapabilityMetadata HealthMetadata = new(
        Name: "system.health",
        Command: "/health",
        Description: "Returns basic engine health",
        Permission: "PUBLIC",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user asks if the bot is alive or for diagnostics",
        IntentHints: ["health", "ping", "alive"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata StatusMetadata = new(
        Name: "system.status",
        Command: "/status",
        Description: "Returns engine runtime status and loaded plugins",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user asks about current system state",
        IntentHints: ["status", "runtime", "jobs"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata PingMetadata = new(
        Name: "bot.ping",
        Command: "/ping",
        Description: "Responds with pong",
        Permission: "PUBLIC",
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata CapabilitiesMetadata = new(
        Name: "system.capabilities",
        Command: "/capabilities",
        Description: "Lists capabilities available to the current user",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user asks what commands or actions are available",
        IntentHints: ["capabilities", "commands", "help"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata HelpMetadata = new(
        Name: "system.help",
        Command: "/help",
        Description: "Show available commands for the current user",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata LlmStatusMetadata = new(
        Name: "system.llm_status",
        Command: "/llm_status",
        Description: "Show configured LLM planner/executor/responder models",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata DiskUsageMetadata = new(
        Name: "system.disk_usage",
        Command: "/disk_usage",
        Description: "Show disk usage for the media root drive",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata FindLargeFilesMetadata = new(
        Name: "system.find_large_files",
        Command: "/find_large_files",
        Description: "Find large files under the media root",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        IsReadOnly: true);
}