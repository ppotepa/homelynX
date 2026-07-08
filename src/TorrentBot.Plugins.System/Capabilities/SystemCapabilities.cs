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
        LlmUsage: "Use when the user asks about current system state, runtime info, or loaded plugins",
        IntentHints: ["status", "runtime", "system info", "stan systemu"],
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
        Description: "Lists capabilities available to the current user. Supports optional 'filter' parameter to narrow results (e.g. by 'download', 'torrent').",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user asks what commands or actions are available, or what commands exist for downloads/pobrania/torrents. Pass filter e.g. {\"filter\": \"download\"} or {\"filter\": \"torrent\"} to get only relevant subset instead of all.",
        IntentHints: ["capabilities", "commands", "help", "jakie komendy", "pobierania", "komendy do", "download commands", "torrent commands", "polecenia"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata HelpMetadata = new(
        Name: "system.help",
        Command: "/help",
        Description: "Show available commands for the current user. Supports optional 'filter' parameter to narrow results (e.g. by 'download', 'torrent').",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user asks what commands exist, how to use the bot, or needs a command reference, especially scoped like \"jakie sa komendy do pobierania\". Pass filter e.g. {\"filter\": \"download\"} or {\"filter\": \"torrent\"} to list only matching commands.",
        IntentHints: ["help", "commands", "list commands", "what can you do", "jakie komendy", "pobierania", "komendy do", "download commands", "torrent komendy", "polecenia do"],
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
        Description: "Show disk usage for the media root drive. ONLY for storage/disk space questions. Never use for content or torrent searches.",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use strictly when user asks about free space, disk usage, storage. Do not confuse with 'search for torrents' or content search.",
        IntentHints: ["disk", "storage", "space", "du", "disk usage", "ile miejsca"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata FindLargeFilesMetadata = new(
        Name: "system.find_large_files",
        Command: "/find_large_files",
        Description: "Find large files under the media root. ONLY for disk analysis of big files. Never for torrent or content search.",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use only for questions about finding large files on disk for cleanup. Not related to downloading or searching torrents.",
        IntentHints: ["large files", "big files", "find large", "du -h", "space hogs"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata LlmPromptMetadata = new(
        Name: "system.llm_prompt",
        Command: "/llm_prompt",
        Description: "Debug: build and return the FULL system/metaprompt that the LLM planner receives for a given user text. Use for manual testing and prompt engineering.",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user (developer) wants to inspect exactly what system prompt, capabilities list, rules, and context the LLM planner sees. Returns the raw prompt text.",
        IntentHints: ["llm prompt", "system prompt", "metaprompt", "debug prompt", "show prompt", "prompt dump", "jakis prompt"],
        IsReadOnly: true);
}