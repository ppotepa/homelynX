using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Downloads.Capabilities;

internal static class DownloadContracts
{
    public static readonly CapabilityContract List = new(
        Name: "download.list",
        ExactSemantics: "List active and recent downloads with name, status, progress, speed, and ETA.",
        Parameters: [],
        Risk: RiskLevel.Safe,
        ResponseSpec: new ResponseConstructionSpec("list", FormatHint: "downloads", ItemsKey: "downloads"),
        Description: "Lists active and recent downloads with rich details.",
        LlmUsage: "Use when user asks 'pokaż pobierania', 'show downloads', 'status pobierania'.",
        IntentHints: ["downloads", "list", "pobierania", "status"],
        IsReadOnly: true);

    public static readonly CapabilityContract Search = new(
        Name: "download.search",
        ExactSemantics: "Search for downloadable content across providers.",
        Parameters:
        [
            new ParameterSpec("query", "string", "Search terms", Required: true),
            new ParameterSpec("provider", "string", "Downloader type: torrent or media", DefaultValue: "torrent")
        ],
        Risk: RiskLevel.Safe,
        ResponseSpec: new ResponseConstructionSpec("search_results", ItemsKey: "results", QueryKey: "query"),
        IsReadOnly: true);

    public static readonly CapabilityContract Start = new(
        Name: "download.start",
        ExactSemantics: "Start a torrent download after selection.",
        Parameters: [],
        Risk: RiskLevel.ConfirmationRequired,
        UserInteractions: new UserInteractionSpec(
            RequiresConfirmation: true,
            ConfirmationMessage: "Start this download?",
            ExpectedResponseTypes: ["confirm", "cancel"]),
        ResponseSpec: new ResponseConstructionSpec("download_started", SelectedKey: "selected"),
        LlmUsage: "Use after search to start a selected download.",
        IntentHints: ["download", "start", "pobierz"]);

    public static readonly CapabilityContract Pause = new(
        Name: "download.pause",
        ExactSemantics: "Pause an active download.",
        Parameters: [],
        Risk: RiskLevel.Safe,
        LlmUsage: "Use for 'pause the download', 'pauzuj', 'wstrzymaj'.",
        IntentHints: ["pause", "pauzuj", "wstrzymaj"]);

    public static readonly CapabilityContract Resume = new(
        Name: "download.resume",
        ExactSemantics: "Resume a paused download.",
        Parameters: [],
        Risk: RiskLevel.Safe,
        LlmUsage: "Use for 'resume the download', 'wznów'.",
        IntentHints: ["resume", "wznów"]);

    public static readonly CapabilityContract Cancel = new(
        Name: "download.cancel",
        ExactSemantics: "Cancel and remove a download.",
        Parameters: [],
        Risk: RiskLevel.Destructive,
        UserInteractions: new UserInteractionSpec(RequiresConfirmation: true),
        IntentHints: ["cancel", "anuluj"]);

    public static readonly CapabilityContract StartMedia = new(
        Name: "download.start_media",
        ExactSemantics: "Download and convert a public video URL to MP3 or MP4.",
        Parameters:
        [
            new ParameterSpec("url", "string", "Public media URL", Required: true),
            new ParameterSpec("format", "string", "mp3 or mp4", DefaultValue: "mp4"),
            new ParameterSpec("quality", "string", "128/192/320 for MP3 or 360/480/720/1080 for MP4"),
            new ParameterSpec("clipStart", "string", "Optional clip start: SS, MM:SS or HH:MM:SS"),
            new ParameterSpec("clipEnd", "string", "Optional clip end: SS, MM:SS or HH:MM:SS"),
            new ParameterSpec("subtitles", "string", "Optional comma-separated languages, e.g. en,pl,auto")
        ],
        Risk: RiskLevel.Safe,
        ResponseSpec: new ResponseConstructionSpec("download_started"),
        Description: "Downloads MP3/MP4 or SRT subtitles from a public media URL; optionally cuts a time range.",
        IntentHints: ["youtube", "facebook", "tiktok", "video", "mp3", "mp4"]);
}
