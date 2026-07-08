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
        Description: "Unified download state across torrent and URL providers. Includes rich progress, speeds (bytes/s), ETA (seconds), category.",
        Fields:
        [
            new QueryFieldMeta("id", "string"),
            new QueryFieldMeta("name", "string"),
            new QueryFieldMeta("provider", "string"),
            new QueryFieldMeta("status", "string"),
            new QueryFieldMeta("progress", "number"),
            new QueryFieldMeta("size", "number"),
            new QueryFieldMeta("downloaded", "number"),
            new QueryFieldMeta("dlspeed", "number"),
            new QueryFieldMeta("upspeed", "number"),
            new QueryFieldMeta("category", "string"),
            new QueryFieldMeta("eta", "number")
        ],
        LlmUsage: "Use to inspect active, paused, and completed downloads WITH details: name, progress (%), dlspeed/upspeed, eta (seconds remaining), status (downloading/paused/seeding etc). Prefer this for 'pokaż pobierania', 'status downloadów', 'jaki postęp'.",
        ExampleQueries:
        [
            "{ \"source\": \"downloads\", \"where\": [{ \"field\": \"status\", \"op\": \"=\", \"value\": \"downloading\" }] }"
        ]);

    public async Task<object> GetSnapshotAsync(CancellationToken ct = default)
    {
        var rows = new List<Dictionary<string, object?>>();

        var torrents = await _qBittorrent.ListTorrentsAsync(ct).ConfigureAwait(false);
        rows.AddRange(torrents.Select(t =>
        {
            long? eta = null;
            if (t.DownloadSpeed > 0 && t.SizeBytes > t.DownloadedBytes)
            {
                eta = (long)((t.SizeBytes - t.DownloadedBytes) / Math.Max(t.DownloadSpeed, 1));
            }

            return new Dictionary<string, object?>
            {
                ["id"] = t.Hash,
                ["name"] = t.Name,
                ["provider"] = "torrent",
                ["status"] = t.Paused ? "paused" : t.State,
                ["progress"] = Math.Round(t.Progress, 1),
                ["size"] = t.SizeBytes,
                ["downloaded"] = t.DownloadedBytes,
                ["dlspeed"] = t.DownloadSpeed,
                ["upspeed"] = t.UploadSpeed,
                ["category"] = string.IsNullOrWhiteSpace(t.Category) ? null : t.Category,
                ["eta"] = eta
            };
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