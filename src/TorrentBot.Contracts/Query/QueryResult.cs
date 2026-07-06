namespace TorrentBot.Contracts.Query;

public sealed record QueryResult(
    string Source,
    IReadOnlyList<Dictionary<string, object?>> Items,
    int Count,
    string? Summary = null);