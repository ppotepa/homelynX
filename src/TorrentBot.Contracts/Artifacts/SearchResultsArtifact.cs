namespace TorrentBot.Contracts.Artifacts;

public sealed record SearchResultsArtifact(
    string Query,
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<SearchResultItem> Items,
    bool HasMore,
    int TotalPages) : IExecutionArtifact
{
    public string Kind => "search_results";
}