using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Downloads.Capabilities;

internal static class DownloadCapabilities
{
    public static readonly CapabilityMetadata ListMetadata = new(
        Name: "download.list",
        Command: "/downloads",
        Description: "Lists active and recent downloads",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user asks what downloads are active or recent",
        IntentHints: ["downloads", "list", "active"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata SearchMetadata = new(
        Name: "download.search",
        Command: "/download_search",
        Description: "Search for downloadable content across providers",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user wants to find content to download",
        IntentHints: ["search", "find", "pobierz"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata StartMetadata = new(
        Name: "download.start",
        Command: "/download",
        Description: "Start a download from torrent or URL",
        Permission: "USER",
        Risk: RiskLevel.ConfirmationRequired,
        LlmUsage: "Use after search to start a selected download",
        IntentHints: ["download", "start", "pobierz"],
        IsLongRunning: true);

    public static readonly CapabilityMetadata PauseMetadata = new(
        Name: "download.pause",
        Command: "/pause",
        Description: "Pause a download",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user wants to pause an active download",
        IntentHints: ["pause", "wstrzymaj"]);

    public static readonly CapabilityMetadata ResumeMetadata = new(
        Name: "download.resume",
        Command: "/resume",
        Description: "Resume a paused download",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user wants to resume a paused download",
        IntentHints: ["resume", "wznow"]);

    public static readonly CapabilityMetadata CancelMetadata = new(
        Name: "download.cancel",
        Command: "/cancel",
        Description: "Cancel and remove a download",
        Permission: "USER",
        Risk: RiskLevel.Destructive,
        LlmUsage: "Use when the user wants to cancel a download",
        IntentHints: ["cancel", "stop", "anuluj"]);

    public static readonly CapabilityMetadata StartUrlMetadata = new(
        Name: "download.start_url",
        Command: "/download_url",
        Description: "Start a direct URL download",
        Permission: "USER",
        Risk: RiskLevel.ConfirmationRequired,
        LlmUsage: "Use when the user provides an HTTP/HTTPS download link",
        IntentHints: ["url", "link", "http", "download"],
        IsLongRunning: true);
}