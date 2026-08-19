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
        IsReadOnly: true);

    public static readonly CapabilityMetadata SearchMetadata = new(
        Name: "download.search",
        Command: "/download_search",
        Description: "Search for downloadable content across providers",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata StartMetadata = new(
        Name: "download.start",
        Command: "/download",
        Description: "Start a download from torrent or URL",
        Permission: "USER",
        Risk: RiskLevel.ConfirmationRequired,
        IsLongRunning: true);

    public static readonly CapabilityMetadata PauseMetadata = new(
        Name: "download.pause",
        Command: "/pause",
        Description: "Pause a download by its current context or identifier.",
        Permission: "USER",
        Risk: RiskLevel.Safe);

    public static readonly CapabilityMetadata ResumeMetadata = new(
        Name: "download.resume",
        Command: "/resume",
        Description: "Resume a paused download by its current context or identifier.",
        Permission: "USER",
        Risk: RiskLevel.Safe);

    public static readonly CapabilityMetadata CancelMetadata = new(
        Name: "download.cancel",
        Command: "/cancel",
        Description: "Cancel and remove a download",
        Permission: "USER",
        Risk: RiskLevel.Destructive);

    public static readonly CapabilityMetadata StartUrlMetadata = new(
        Name: "download.start_url",
        Command: "/download_url",
        Description: "Start a direct URL download",
        Permission: "USER",
        Risk: RiskLevel.ConfirmationRequired,
        IsLongRunning: true);
}
