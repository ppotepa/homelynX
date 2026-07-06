using TorrentBot.Integrations.Interfaces;
using TorrentBot.Plugins.Downloads.Downloaders;
using TorrentBot.Plugins.Downloads.ProcessManagers;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Plugins.Downloads.Snapshots;

public sealed class DownloadsSnapshotSource : ISnapshotSource
{
    private readonly IQBittorrentClient _qBittorrent;
    private readonly UrlDownloader _urlDownloader;
    private readonly DownloadProcessManager _processManager;

    public DownloadsSnapshotSource(
        IQBittorrentClient qBittorrent,
        UrlDownloader urlDownloader,
        DownloadProcessManager processManager)
    {
        _qBittorrent = qBittorrent;
        _urlDownloader = urlDownloader;
        _processManager = processManager;
    }

    public string Name => "downloads";

    public QuerySourceMeta GetManifest() => new(
        Name: Name,
        Description: "Unified download state across torrent and URL providers",
        Fields:
        [
            new QueryFieldMeta("id", "string"),
            new QueryFieldMeta("name", "string"),
            new QueryFieldMeta("provider", "string"),
            new QueryFieldMeta("status", "string"),
            new QueryFieldMeta("progress", "number"),
            new QueryFieldMeta("size", "number")
        ],
        LlmUsage: "Use to inspect active, paused, and completed downloads",
        ExampleQueries:
        [
            "{ \"source\": \"downloads\", \"where\": [{ \"field\": \"status\", \"op\": \"=\", \"value\": \"downloading\" }] }"
        ]);

    public async Task<object> GetSnapshotAsync(CancellationToken ct = default)
    {
        var rows = new List<Dictionary<string, object?>>();

        var torrents = await _qBittorrent.ListTorrentsAsync(ct).ConfigureAwait(false);
        rows.AddRange(torrents.Select(t => new Dictionary<string, object?>
        {
            ["id"] = t.Hash,
            ["name"] = t.Name,
            ["provider"] = "torrent",
            ["status"] = t.Paused ? "paused" : t.State,
            ["progress"] = t.Progress,
            ["size"] = t.SizeBytes,
            ["downloaded"] = t.DownloadedBytes
        }));

        rows.AddRange(_urlDownloader.GetSnapshotRows());

        var managedRows = await _processManager.GetDownloadSnapshotRowsAsync(ct).ConfigureAwait(false);
        foreach (var row in managedRows)
        {
            if (!rows.Any(existing => string.Equals(existing["id"]?.ToString(), row["id"]?.ToString(), StringComparison.Ordinal)))
            {
                rows.Add(row);
            }
        }

        return rows;
    }
}