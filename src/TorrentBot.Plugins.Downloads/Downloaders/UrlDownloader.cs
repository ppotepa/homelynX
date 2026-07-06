using System.Collections.Concurrent;

namespace TorrentBot.Plugins.Downloads.Downloaders;

public sealed class UrlDownloader : IDownloader
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, UrlDownloadEntry> _downloads = new(StringComparer.Ordinal);

    public UrlDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public string Type => "url";
    public string DisplayName => "Direct URL";

    public Task<DownloadSearchResults> SearchAsync(DownloadSearchRequest request, CancellationToken ct = default) =>
        Task.FromResult(new DownloadSearchResults([]));

    public async Task<DownloadTicket> StartAsync(DownloadStartRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Url);

        var downloadId = $"url-{Guid.NewGuid():N}";
        var name = Path.GetFileName(new Uri(request.Url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "download";
        }

        long sizeBytes = 0;
        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, request.Url);
            using var headResponse = await _httpClient.SendAsync(headRequest, ct).ConfigureAwait(false);
            sizeBytes = headResponse.Content.Headers.ContentLength ?? 0;
        }
        catch (HttpRequestException)
        {
            // HEAD may not be supported; proceed with unknown size.
        }

        _downloads[downloadId] = new UrlDownloadEntry(
            downloadId,
            name,
            request.Url!,
            sizeBytes,
            "downloading",
            0);

        _ = Task.Run(() => DownloadInBackgroundAsync(downloadId, request.Url!, sizeBytes, CancellationToken.None));

        return new DownloadTicket(downloadId, Type, name);
    }

    public Task<DownloadStatus> GetStatusAsync(string downloadId, CancellationToken ct = default)
    {
        if (!_downloads.TryGetValue(downloadId, out var entry))
        {
            throw new KeyNotFoundException($"URL download '{downloadId}' was not found.");
        }

        return Task.FromResult(new DownloadStatus(
            entry.Id,
            Type,
            entry.Name,
            entry.Status,
            entry.Progress,
            entry.SizeBytes,
            entry.DownloadedBytes));
    }

    public Task PauseAsync(string downloadId, CancellationToken ct = default)
    {
        Update(downloadId, e => e with { Status = "paused" });
        return Task.CompletedTask;
    }

    public Task ResumeAsync(string downloadId, CancellationToken ct = default)
    {
        Update(downloadId, e => e with { Status = "downloading" });
        return Task.CompletedTask;
    }

    public Task CancelAsync(string downloadId, CancellationToken ct = default)
    {
        Update(downloadId, e => e with { Status = "cancelled" });
        _downloads.TryRemove(downloadId, out _);
        return Task.CompletedTask;
    }

    internal IReadOnlyList<Dictionary<string, object?>> GetSnapshotRows() =>
        _downloads.Values
            .Select(entry => new Dictionary<string, object?>
            {
                ["id"] = entry.Id,
                ["name"] = entry.Name,
                ["provider"] = Type,
                ["status"] = entry.Status,
                ["progress"] = entry.Progress,
                ["size"] = entry.SizeBytes,
                ["url"] = entry.Url
            })
            .ToList();

    private async Task DownloadInBackgroundAsync(
        string downloadId,
        string url,
        long knownSizeBytes,
        CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? knownSizeBytes;
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            long downloaded = 0;

            while (true)
            {
                if (!_downloads.TryGetValue(downloadId, out var current))
                {
                    return;
                }

                if (string.Equals(current.Status, "paused", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(250, ct).ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(current.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
                downloaded += read;
                var progress = totalBytes > 0 ? Math.Min(1.0, downloaded / (double)totalBytes) : 0.0;
                Update(downloadId, e => e with
                {
                    DownloadedBytes = downloaded,
                    SizeBytes = totalBytes > 0 ? totalBytes : e.SizeBytes,
                    Progress = progress,
                    Status = "downloading"
                });
            }

            Update(downloadId, e => e with
            {
                DownloadedBytes = downloaded,
                SizeBytes = totalBytes > 0 ? totalBytes : downloaded,
                Progress = 1.0,
                Status = "completed"
            });
        }
        catch (Exception)
        {
            Update(downloadId, e => e with { Status = "failed" });
        }
    }

    private void Update(string downloadId, Func<UrlDownloadEntry, UrlDownloadEntry> updater)
    {
        if (_downloads.TryGetValue(downloadId, out var current))
        {
            _downloads[downloadId] = updater(current);
        }
    }

    private sealed record UrlDownloadEntry(
        string Id,
        string Name,
        string Url,
        long SizeBytes,
        string Status,
        double Progress,
        long DownloadedBytes = 0);
}