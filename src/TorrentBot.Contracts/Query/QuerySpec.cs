namespace TorrentBot.Contracts.Query;

public sealed record QueryWhere(string Field, string Op, object? Value);

public sealed record QueryOrderBy(string Field, bool Descending = false);

public sealed record QueryAggregate(string Function, string Field, string? Alias = null);

public sealed record QuerySpec(
    string Source = "downloads",
    IReadOnlyList<QueryWhere>? Where = null,
    IReadOnlyList<string>? Select = null,
    IReadOnlyList<QueryOrderBy>? OrderBy = null,
    IReadOnlyList<string>? GroupBy = null,
    IReadOnlyList<QueryAggregate>? Aggregate = null,
    int Limit = 20);