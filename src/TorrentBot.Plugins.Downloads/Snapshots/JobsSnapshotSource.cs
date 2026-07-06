using TorrentBot.Plugins.Downloads.ProcessManagers;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Plugins.Downloads.Snapshots;

public sealed class JobsSnapshotSource : ISnapshotSource
{
    private readonly DownloadProcessManager _processManager;

    public JobsSnapshotSource(DownloadProcessManager processManager)
    {
        _processManager = processManager;
    }

    public string Name => "jobs";

    public QuerySourceMeta GetManifest() => new(
        Name: Name,
        Description: "Download process job projections",
        Fields:
        [
            new QueryFieldMeta("id", "string"),
            new QueryFieldMeta("type", "string"),
            new QueryFieldMeta("status", "string"),
            new QueryFieldMeta("progress", "number"),
            new QueryFieldMeta("name", "string"),
            new QueryFieldMeta("provider", "string")
        ],
        LlmUsage: "Use to inspect long-lived download jobs and their progress",
        ExampleQueries:
        [
            "{ \"source\": \"jobs\", \"where\": [{ \"field\": \"status\", \"op\": \"=\", \"value\": \"running\" }] }"
        ]);

    public Task<object> GetSnapshotAsync(CancellationToken ct = default) =>
        Task.FromResult<object>(_processManager.GetJobSnapshotRows());
}