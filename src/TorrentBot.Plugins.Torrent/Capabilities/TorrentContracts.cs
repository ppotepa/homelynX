using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Torrent.Capabilities;

internal static class TorrentContracts
{
    public static readonly CapabilityContract Search = new(
        Name: "torrent.search",
        ExactSemantics: "Search torrent indexers via Jackett. Returns numbered results for later selection by index.",
        Parameters: [new ParameterSpec("query", "string", "Search terms", Required: true)],
        Risk: RiskLevel.Safe,
        UserInteractions: new UserInteractionSpec(
            ExpectedResponseTypes: ["index", "text"],
            PromptText: "Select a result by index or search again."),
        ResponseSpec: new ResponseConstructionSpec("search_results", UseConversationState: true, ItemsKey: "results", QueryKey: "query"),
        Continuations:
        [
            new ContinuationRule(
                Trigger: "when_has_results",
                ActionType: "await_indexed_choice",
                ExpectedResponse: new ExpectedResponseShape("index", "index", Examples: ["1", "2", "select 1"]),
                NextCapability: "torrent.select_result")
        ],
        Description: "Search torrent indexers via Jackett for a given query string.",
        LlmUsage: "Use for content/torrent search requests like 'search for ubuntu', 'znajdz ubuntu'.",
        IntentHints: ["search", "znajdz", "find", "pobierz", "szukaj"],
        IsReadOnly: true);

    public static readonly CapabilityContract List = new(
        Name: "torrent.list",
        ExactSemantics: "List qBittorrent torrents with progress, speeds, and state.",
        Parameters: [],
        Risk: RiskLevel.Safe,
        ResponseSpec: new ResponseConstructionSpec("list", FormatHint: "rich_status", ItemsKey: "torrents"),
        Description: "List torrents managed by qBittorrent with rich details.",
        LlmUsage: "Use when user asks 'pokaż torrenty', 'list torrents', 'status torrenty'.",
        IntentHints: ["torrents", "list", "status torrenty"],
        IsReadOnly: true);

    public static readonly CapabilityContract SelectResult = new(
        Name: "torrent.select_result",
        ExactSemantics: "Select a numbered search result and start download.",
        Parameters: [new ParameterSpec("index", "int", "1-based index matching displayed search result numbers", Required: true)],
        Risk: RiskLevel.ConfirmationRequired,
        UserInteractions: new UserInteractionSpec(
            RequiresConfirmation: true,
            ConfirmationMessage: "Start download for selected torrent?",
            ExpectedResponseTypes: ["confirm", "cancel"]),
        ResponseSpec: new ResponseConstructionSpec("download_started", SelectedKey: "selected"),
        Continuations:
        [
            new ContinuationRule(
                Trigger: "on_success",
                ActionType: "await_confirm",
                ExpectedResponse: new ExpectedResponseShape("yes_no"),
                NextCapability: "torrent.select_result")
        ],
        LlmUsage: "Use after torrent.search when user says 'select 1', 'wybierz pierwszy' (indexes match displayed numbers).",
        IntentHints: ["select", "wybierz", "first", "pierwszy"]);

    public static readonly CapabilityContract MoreResults = new(
        Name: "torrent.more_results",
        ExactSemantics: "Show next page of torrent search results.",
        Parameters: [],
        Risk: RiskLevel.Safe,
        ResponseSpec: new ResponseConstructionSpec("search_results", ItemsKey: "results", QueryKey: "query"),
        IsReadOnly: true);

    public static readonly CapabilityContract CancelSearch = new(
        Name: "torrent.cancel_search",
        ExactSemantics: "Cancel active torrent search session.",
        Parameters: [],
        Risk: RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityContract DownloadCandidate = new(
        Name: "torrent.download_candidate",
        ExactSemantics: "Search and auto-start best torrent candidate for a title.",
        Parameters: [new ParameterSpec("title", "string", "Title to search", Required: true)],
        Risk: RiskLevel.ConfirmationRequired,
        UserInteractions: new UserInteractionSpec(RequiresConfirmation: true),
        ResponseSpec: new ResponseConstructionSpec("download_started", SelectedKey: "selected"),
        LlmUsage: "Use when user wants to download by title without manual selection.",
        IntentHints: ["download", "candidate", "pobierz"]);

    public static readonly CapabilityContract Pause = new(
        Name: "torrent.pause",
        ExactSemantics: "Pause a torrent in qBittorrent by hash.",
        Parameters: [new ParameterSpec("hash", "string", "Torrent hash", Required: true)],
        Risk: RiskLevel.Safe);

    public static readonly CapabilityContract Resume = new(
        Name: "torrent.resume",
        ExactSemantics: "Resume a paused torrent in qBittorrent by hash.",
        Parameters: [new ParameterSpec("hash", "string", "Torrent hash", Required: true)],
        Risk: RiskLevel.Safe);

    public static readonly CapabilityContract Delete = new(
        Name: "torrent.delete",
        ExactSemantics: "Delete a torrent from qBittorrent.",
        Parameters:
        [
            new ParameterSpec("hash", "string", "Torrent hash", Required: true),
            new ParameterSpec("deleteFiles", "bool", "Also delete files")
        ],
        Risk: RiskLevel.Destructive,
        UserInteractions: new UserInteractionSpec(RequiresConfirmation: true));
}