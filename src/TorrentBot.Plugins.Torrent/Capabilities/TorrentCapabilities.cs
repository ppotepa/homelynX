using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Torrent.Capabilities;

internal static class TorrentCapabilities
{
    public static readonly CapabilityMetadata SearchMetadata = new(
        Name: "torrent.search",
        Command: "/search",
        Description: "Search torrent indexers via Jackett for a given query string. Returns numbered list of results the user can later select by index.",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use for content/torrent search requests like 'search for ubuntu', 'znajdz ubuntu', 'pobierz ubuntu'. Never confuse with disk usage or find large files.",
        IntentHints: ["search", "znajdz", "find", "pobierz", "szukaj", "torrents for", "torrent search"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata ListMetadata = new(
        Name: "torrent.list",
        Command: "/torrents",
        Description: "List torrents managed by qBittorrent with rich details (progress, speeds, state)",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when user says 'pokaż torrenty', 'list torrents', 'status torrenty', 'pokaż status torrenty', 'co się pobiera', 'status qbit'. Provides rich name, %, speed, state for each.",
        IntentHints: ["torrents", "list", "qbittorrent", "pokaż torrenty", "torrenty", "status torrenty", "pokaż status torrenty"],
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
        Description: "Select a numbered torrent search result to download. Use the numeric index from the most recent torrent.search results list.",
        Permission: "USER",
        Risk: RiskLevel.ConfirmationRequired,
        LlmUsage: "Use after a recent torrent.search (look for search results in conversation history or snapshots). Maps 'select 1', 'select the first', 'wybierz pierwszy' etc. to 1-based displayed index.",
        IntentHints: ["select", "wybierz", "first", "1", "pick", "the first", "select 1", "wybierz 1", "pierwszy"]);

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
        IntentHints: ["download", "candidate", "best", "pobierz", "pobranie"]);
}