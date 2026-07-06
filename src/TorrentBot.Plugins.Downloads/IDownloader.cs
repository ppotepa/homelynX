namespace TorrentBot.Plugins.Downloads;

public interface IDownloader
{
    string Type { get; }
    string DisplayName { get; }

    Task<DownloadSearchResults> SearchAsync(DownloadSearchRequest request, CancellationToken ct = default);
    Task<DownloadTicket> StartAsync(DownloadStartRequest request, CancellationToken ct = default);
    Task<DownloadStatus> GetStatusAsync(string downloadId, CancellationToken ct = default);
    Task PauseAsync(string downloadId, CancellationToken ct = default);
    Task ResumeAsync(string downloadId, CancellationToken ct = default);
    Task CancelAsync(string downloadId, CancellationToken ct = default);
}

public sealed record DownloadSearchRequest(string Query, string? Provider = null);

public sealed record DownloadSearchResult(
    string Id,
    string Name,
    string Provider,
    long SizeBytes,
    int Seeders,
    string? MagnetUri,
    string? Url);

public sealed record DownloadSearchResults(IReadOnlyList<DownloadSearchResult> Items);

public sealed record DownloadStartRequest(
    string Provider,
    string? Url = null,
    string? Magnet = null,
    string? Query = null,
    int? SearchIndex = null,
    string? Category = null,
    string? SavePath = null);

public sealed record DownloadTicket(string DownloadId, string Provider, string Name);

public sealed record DownloadStatus(
    string DownloadId,
    string Provider,
    string Name,
    string Status,
    double Progress,
    long SizeBytes,
    long DownloadedBytes);