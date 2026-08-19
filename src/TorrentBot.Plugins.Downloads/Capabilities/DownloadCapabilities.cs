using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Downloads.Capabilities;

internal static class DownloadCapabilities
{
    public static readonly CapabilityMetadata ListMetadata = new(
        Name: "download.list",
        Command: "/downloads",
        Description: "Lists active and recent downloads with rich details: name, status, progress %, dlspeed, eta",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user asks 'pokaż pobierania', 'show downloads', 'status pobierania', 'jaki postęp'. Returns name, progress, speed, eta, status.",
        IntentHints: ["downloads", "list", "active", "pobierania", "pokaż pobierania", "status", "postęp"],
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
        Description: "Pause a download. Use after a download has been started in the conversation or from context/snapshots of active downloads.",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use for 'pause the download', 'pauzuj', 'pause it', 'wstrzymaj pobieranie' etc. Refer to previous search/start or downloads snapshot.",
        IntentHints: ["pause", "wstrzymaj", "pauzuj", "pause the", "pause download", "pause it"]);

    public static readonly CapabilityMetadata ResumeMetadata = new(
        Name: "download.resume",
        Command: "/resume",
        Description: "Resume a paused download. Use after pause in conversation or from active/paused state in snapshots.",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use for 'resume the download', 'wznów', 'resume it' after a prior pause or from context.",
        IntentHints: ["resume", "wznow", "wznów", "resume the", "resume download"]);

    public static readonly CapabilityMetadata CancelMetadata = new(
        Name: "download.cancel",
        Command: "/cancel",
        Description: "Cancel and remove a download",
        Permission: "USER",
        Risk: RiskLevel.Destructive,
        LlmUsage: "Use when the user wants to cancel a download",
        IntentHints: ["cancel", "stop", "anuluj"]);

    public static readonly CapabilityMetadata StartMediaMetadata = new(
        Name: "download.start_media",
        Command: "/download_media",
        Description: "Download and convert a public media URL",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user provides a YouTube, Facebook, Dailymotion, Vimeo, Instagram or TikTok URL",
        IntentHints: ["youtube", "facebook", "tiktok", "video", "mp3", "mp4"],
        IsLongRunning: true);
}
