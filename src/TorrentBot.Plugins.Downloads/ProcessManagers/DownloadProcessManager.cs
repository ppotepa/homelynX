using System.Collections.Concurrent;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Jobs;
using TorrentBot.Contracts.ProcessManagers;

namespace TorrentBot.Plugins.Downloads.ProcessManagers;

public sealed class DownloadProcessManager : IDownloadProcessManager
{
    private readonly DownloaderRegistry _registry;
    private readonly ConcurrentDictionary<string, ManagedDownload> _processes = new(StringComparer.Ordinal);

    public DownloadProcessManager(DownloaderRegistry registry)
    {
        _registry = registry;
    }

    public string ProcessType => "download";

    public async Task<string> StartAsync(object startPayload, IRequestContext context, CancellationToken ct = default)
    {
        var request = ParseStartPayload(startPayload);
        var downloader = _registry.GetRequired(request.Provider);
        var ticket = await downloader.StartAsync(request, ct).ConfigureAwait(false);

        var jobId = $"dl-{Guid.NewGuid():N}";
        _processes[jobId] = new ManagedDownload(
            JobId: jobId,
            DownloadId: ticket.DownloadId,
            Provider: ticket.Provider,
            Name: ticket.Name,
            Status: "running",
            Progress: 0,
            OwnerUserId: context.UserId,
            TraceId: context.TraceId,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        return jobId;
    }

    public async Task HandleCommandAsync(
        string jobId,
        string command,
        object? payload,
        IRequestContext actorContext,
        CancellationToken ct = default)
    {
        if (!_processes.TryGetValue(jobId, out var process))
        {
            throw new KeyNotFoundException($"Download process '{jobId}' was not found.");
        }

        var downloader = _registry.GetRequired(process.Provider);
        switch (command.ToLowerInvariant())
        {
            case "pause":
                await downloader.PauseAsync(process.DownloadId, ct).ConfigureAwait(false);
                UpdateProcess(jobId, p => p with { Status = "paused" });
                break;
            case "resume":
                await downloader.ResumeAsync(process.DownloadId, ct).ConfigureAwait(false);
                UpdateProcess(jobId, p => p with { Status = "running" });
                break;
            case "cancel":
                await downloader.CancelAsync(process.DownloadId, ct).ConfigureAwait(false);
                _processes.TryRemove(jobId, out _);
                break;
            default:
                throw new InvalidOperationException($"Unsupported download command '{command}'.");
        }
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetDownloadSnapshotRowsAsync(CancellationToken ct = default)
    {
        var rows = new List<Dictionary<string, object?>>();

        foreach (var downloader in _registry.GetAll())
        {
            if (downloader is Downloaders.UrlDownloader urlDownloader)
            {
                rows.AddRange(urlDownloader.GetSnapshotRows());
                continue;
            }

            if (downloader.Type.Equals("torrent", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var process in _processes.Values.Where(p => p.Provider == "torrent"))
                {
                    try
                    {
                        var status = await downloader.GetStatusAsync(process.DownloadId, ct).ConfigureAwait(false);
                        rows.Add(new Dictionary<string, object?>
                        {
                            ["id"] = status.DownloadId,
                            ["name"] = status.Name,
                            ["provider"] = status.Provider,
                            ["status"] = status.Status,
                            ["progress"] = status.Progress,
                            ["size"] = status.SizeBytes,
                            ["downloaded"] = status.DownloadedBytes
                        });
                    }
                    catch (KeyNotFoundException)
                    {
                        rows.Add(process.ToDownloadRow());
                    }
                }
            }
        }

        return rows;
    }

    public IReadOnlyList<Dictionary<string, object?>> GetTrackedProcessRows() => GetJobSnapshotRows();

    public async Task SyncDownloadStatusesAsync(CancellationToken ct = default)
    {
        foreach (var process in _processes.Values.ToList())
        {
            var downloader = _registry.GetRequired(process.Provider);
            try
            {
                var status = await downloader.GetStatusAsync(process.DownloadId, ct).ConfigureAwait(false);
                UpdateProcess(process.JobId, current => current with
                {
                    Status = status.Status,
                    Progress = status.Progress
                });
            }
            catch (KeyNotFoundException)
            {
                // Process may have been removed after completion.
            }
        }
    }

    public IReadOnlyList<Dictionary<string, object?>> GetJobSnapshotRows() =>
        _processes.Values
            .Select(p => new Dictionary<string, object?>
            {
                ["id"] = p.JobId,
                ["type"] = $"download.{p.Provider}",
                ["status"] = p.Status,
                ["progress"] = p.Progress,
                ["name"] = p.Name,
                ["provider"] = p.Provider,
                ["downloadId"] = p.DownloadId,
                ["ownerUserId"] = p.OwnerUserId,
                ["traceId"] = p.TraceId,
                ["createdAtUtc"] = p.CreatedAtUtc
            })
            .ToList();

    private void UpdateProcess(string jobId, Func<ManagedDownload, ManagedDownload> updater)
    {
        if (_processes.TryGetValue(jobId, out var current))
        {
            _processes[jobId] = updater(current);
        }
    }

    private static DownloadStartRequest ParseStartPayload(object startPayload) => startPayload switch
    {
        DownloadStartRequest request => request,
        IReadOnlyDictionary<string, object?> dict => new DownloadStartRequest(
            Provider: GetString(dict, "provider") ?? "torrent",
            Url: GetString(dict, "url"),
            Magnet: GetString(dict, "magnet"),
            Query: GetString(dict, "query"),
            SearchIndex: GetInt(dict, "index") ?? GetInt(dict, "searchIndex"),
            Category: GetString(dict, "category"),
            SavePath: GetString(dict, "savePath")),
        _ => throw new ArgumentException("Unsupported download start payload.", nameof(startPayload))
    };

    private static string? GetString(IReadOnlyDictionary<string, object?> dict, string key) =>
        dict.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static int? GetInt(IReadOnlyDictionary<string, object?> dict, string key) =>
        dict.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), out var number)
            ? number
            : null;

    private sealed record ManagedDownload(
        string JobId,
        string DownloadId,
        string Provider,
        string Name,
        string Status,
        double Progress,
        string OwnerUserId,
        string TraceId,
        DateTimeOffset CreatedAtUtc)
    {
        public Dictionary<string, object?> ToDownloadRow() => new()
        {
            ["id"] = DownloadId,
            ["name"] = Name,
            ["provider"] = Provider,
            ["status"] = Status,
            ["progress"] = Progress
        };
    }
}