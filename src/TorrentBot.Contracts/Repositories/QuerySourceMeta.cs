namespace TorrentBot.Contracts.Repositories;

public sealed record QueryFieldMeta(string Name, string Type, IReadOnlyList<string>? AllowedOperators = null);

public sealed record QuerySourceMeta(
    string Name,
    string Description,
    IReadOnlyList<QueryFieldMeta> Fields,
    string? LlmUsage = null,
    IReadOnlyList<string>? ExampleQueries = null,
    int DefaultLimit = 20,
    int MaxLimit = 100);