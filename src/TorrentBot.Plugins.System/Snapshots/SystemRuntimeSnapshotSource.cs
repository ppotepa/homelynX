using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Plugins.System.Snapshots;

public sealed class SystemRuntimeSnapshotSource : ISnapshotSource
{
    public string Name => "system.runtime";

    public QuerySourceMeta GetManifest() => new(
        Name: Name,
        Description: "Engine runtime snapshot for safe queries",
        Fields:
        [
            new QueryFieldMeta("component", "string"),
            new QueryFieldMeta("status", "string"),
            new QueryFieldMeta("detail", "string")
        ],
        LlmUsage: "Use to inspect engine/runtime state via query.execute",
        ExampleQueries: ["{ \"source\": \"system.runtime\", \"limit\": 10 }"]);

    public Task<object> GetSnapshotAsync(CancellationToken ct = default) =>
        Task.FromResult<object>(new List<Dictionary<string, object?>>
        {
            new() { ["component"] = "engine", ["status"] = "running", ["detail"] = "core orchestrator" }
        });
}