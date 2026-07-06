using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Torrent.Capabilities;

internal static class TorrentCapabilities
{
    public static readonly CapabilityMetadata SearchMetadata = new(
        Name: "torrent.search",
        Command: "/search",
        Description: "Search torrent indexers via Jackett",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user wants torrent-specific search results",
        IntentHints: ["torrent", "search", "tracker"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata ListMetadata = new(
        Name: "torrent.list",
        Command: "/torrents",
        Description: "List torrents managed by qBittorrent",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user asks about torrent client state",
        IntentHints: ["torrents", "list", "qbittorrent"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata PauseMetadata = new(
        Name: "torrent.pause",
        Command: "/torrent_pause",
        Description: "Pause a torrent in qBittorrent",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use to pause a specific torrent by hash",
        IntentHints: ["pause", "torrent"]);

    public static readonly CapabilityMetadata ResumeMetadata = new(
        Name: "torrent.resume",
        Command: "/torrent_resume",
        Description: "Resume a paused torrent in qBittorrent",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use to resume a paused torrent by hash",
        IntentHints: ["resume", "torrent"]);

    public static readonly CapabilityMetadata DeleteMetadata = new(
        Name: "torrent.delete",
        Command: "/torrent_delete",
        Description: "Delete a torrent from qBittorrent",
        Permission: "USER",
        Risk: RiskLevel.Destructive,
        LlmUsage: "Use to remove a torrent and optionally its files",
        IntentHints: ["delete", "remove", "torrent"]);

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
        Description: "Select a numbered torrent search result to download",
        Permission: "USER",
        Risk: RiskLevel.ConfirmationRequired,
        LlmUsage: "Use after torrent.search when user picks a numbered result");

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
        Risk: RiskLevel.ConfirmationRequired,
        LlmUsage: "Use when user wants to download by title without manual selection",
        IntentHints: ["download", "candidate", "best"]);
}