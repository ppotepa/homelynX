using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Torrent.Capabilities;

internal static class TorrentCapabilities
{
    public static readonly CapabilityMetadata SearchMetadata = new(
        Name: "torrent.search",
        Command: "/search",
        Description: "Search torrent indexers via Jackett for a query and return numbered results for /select.",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata ListMetadata = new(
        Name: "torrent.list",
        Command: "/torrents",
        Description: "List torrents managed by qBittorrent with progress, speeds, and state.",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata PauseMetadata = new(
        Name: "torrent.pause",
        Command: "/torrent_pause",
        Description: "Pause a torrent in qBittorrent",
        Permission: "USER",
        Risk: RiskLevel.Safe);

    public static readonly CapabilityMetadata ResumeMetadata = new(
        Name: "torrent.resume",
        Command: "/torrent_resume",
        Description: "Resume a paused torrent in qBittorrent",
        Permission: "USER",
        Risk: RiskLevel.Safe);

    public static readonly CapabilityMetadata DeleteMetadata = new(
        Name: "torrent.delete",
        Command: "/torrent_delete",
        Description: "Delete a torrent from qBittorrent",
        Permission: "USER",
        Risk: RiskLevel.Destructive);

    public static readonly CapabilityMetadata MoreResultsMetadata = new(
        Name: "torrent.more_results",
        Command: "/more",
        Description: "Show next page of torrent search results",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata SelectResultMetadata = new(
        Name: "torrent.select_result",
        Command: "/select",
        Description: "Select a numbered result from the current torrent search session.",
        Permission: "USER",
        Risk: RiskLevel.ConfirmationRequired);

    public static readonly CapabilityMetadata CancelSearchMetadata = new(
        Name: "torrent.cancel_search",
        Command: "/cancel_search",
        Description: "Cancel the active torrent search session",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata DownloadCandidateMetadata = new(
        Name: "torrent.download_candidate",
        Command: "/download_candidate",
        Description: "Search and auto-start the best torrent candidate for a title",
        Permission: "USER",
        Risk: RiskLevel.ConfirmationRequired);
}
