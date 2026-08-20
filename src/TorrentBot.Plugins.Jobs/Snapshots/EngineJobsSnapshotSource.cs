using TorrentBot.Contracts.Repositories;
using TorrentBot.Engine;

namespace TorrentBot.Plugins.Jobs.Snapshots;

public sealed class EngineJobsSnapshotSource : ISnapshotSource
{
    public string Name => "engine_jobs";

    public QuerySourceMeta GetManifest() => new(
        Name: Name,
        Description: "Engine-level tracked jobs",
        Fields:
        [
            new QueryFieldMeta("id", "string"),
            new QueryFieldMeta("type", "string"),
            new QueryFieldMeta("status", "string"),
            new QueryFieldMeta("progress", "number"),
            new QueryFieldMeta("ownerUserId", "string")
        ],
        ExampleQueries: ["{ \"source\": \"jobs\", \"limit\": 10 }"]);

    public Task<object> GetSnapshotAsync(CancellationToken ct = default) =>
        Task.FromResult<object>(Array.Empty<Dictionary<string, object?>>());
}
