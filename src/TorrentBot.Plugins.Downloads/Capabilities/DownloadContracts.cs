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
        IsReadOnly: true);

    public static readonly CapabilityContract Search = new(
        Name: "download.search",
        ExactSemantics: "Search for downloadable content across providers.",
        Parameters:
        [
            new ParameterSpec("query", "string", "Search terms", Required: true),
            new ParameterSpec("provider", "string", "Downloader type: \"torrent\" or \"url\"", DefaultValue: "torrent")
        ],
        Risk: RiskLevel.Safe,
        ResponseSpec: new ResponseConstructionSpec("search_results", ItemsKey: "results", QueryKey: "query"),
        IsReadOnly: true);

    public static readonly CapabilityContract Start = new(
        Name: "download.start",
        ExactSemantics: "Start a download from torrent or URL after selection.",
        Parameters: [],
        Risk: RiskLevel.ConfirmationRequired,
        UserInteractions: new UserInteractionSpec(
            RequiresConfirmation: true,
            ConfirmationMessage: "Start this download?",
            ExpectedResponseTypes: ["confirm", "cancel"]),
        ResponseSpec: new ResponseConstructionSpec("download_started", SelectedKey: "selected"));

    public static readonly CapabilityContract Pause = new(
        Name: "download.pause",
        ExactSemantics: "Pause an active download.",
        Parameters: [],
        Risk: RiskLevel.Safe);

    public static readonly CapabilityContract Resume = new(
        Name: "download.resume",
        ExactSemantics: "Resume a paused download.",
        Parameters: [],
        Risk: RiskLevel.Safe);

    public static readonly CapabilityContract Cancel = new(
        Name: "download.cancel",
        ExactSemantics: "Cancel and remove a download.",
        Parameters: [],
        Risk: RiskLevel.Destructive,
        UserInteractions: new UserInteractionSpec(RequiresConfirmation: true));

    public static readonly CapabilityContract StartUrl = new(
        Name: "download.start_url",
        ExactSemantics: "Start a direct URL download.",
        Parameters: [new ParameterSpec("url", "string", "HTTP/HTTPS URL", Required: true)],
        Risk: RiskLevel.ConfirmationRequired,
        UserInteractions: new UserInteractionSpec(RequiresConfirmation: true),
        ResponseSpec: new ResponseConstructionSpec("download_started"));

    public static readonly CapabilityContract StartMedia = new(
        Name: "download.start_media",
        ExactSemantics: "Download public media from a supported URL with optional format, quality, subtitles, and clip range.",
        Parameters:
        [
            new ParameterSpec("url", "string", "Public media URL", Required: true),
            new ParameterSpec("provider", "string", "Media provider", DefaultValue: "media"),
            new ParameterSpec("format", "string", "mp3, mp4, or subtitles", DefaultValue: "mp4"),
            new ParameterSpec("quality", "string", "Audio bitrate or video height"),
            new ParameterSpec("clipStart", "string", "Clip start timestamp"),
            new ParameterSpec("clipEnd", "string", "Clip end timestamp"),
            new ParameterSpec("subtitles", "string", "Subtitle language codes")
        ],
        Risk: RiskLevel.ConfirmationRequired,
        UserInteractions: new UserInteractionSpec(
            RequiresConfirmation: true,
            ConfirmationMessage: "Start this media download?",
            ExpectedResponseTypes: ["confirm", "cancel"]),
        ResponseSpec: new ResponseConstructionSpec("download_started"));
}
