using TorrentBot.Integrations.Interfaces;
using TorrentBot.Integrations.Models;

namespace TorrentBot.Plugins.Downloads.Downloaders;

public sealed class TorrentDownloader : IDownloader
{
    private readonly IJackettClient _jackett;
    private readonly IQBittorrentClient _qBittorrent;
    private IReadOnlyList<TorrentSearchResult> _lastSearchResults = [];

    public TorrentDownloader(IJackettClient jackett, IQBittorrentClient qBittorrent)
    {
        _jackett = jackett;
        _qBittorrent = qBittorrent;
    }

    public string Type => "torrent";
    public string DisplayName => "Torrent (Jackett + qBittorrent)";

    public async Task<DownloadSearchResults> SearchAsync(DownloadSearchRequest request, CancellationToken ct = default)
    {
        var results = await _jackett.SearchAsync(request.Query, ct).ConfigureAwait(false);
        _lastSearchResults = results;

        var items = results.Select((result, index) => new DownloadSearchResult(
            Id: $"torrent-{index}",
            Name: result.Title,
            Provider: Type,
            SizeBytes: result.SizeBytes,
            Seeders: result.Seeders,
            MagnetUri: result.MagnetUri,
            Url: result.DownloadUrl)).ToList();

        return new DownloadSearchResults(items);
    }

    public async Task<DownloadTicket> StartAsync(DownloadStartRequest request, CancellationToken ct = default)
    {
        var url = ResolveStartUrl(request);
        var hash = await _qBittorrent.AddTorrentAsync(
            new AddTorrentRequest(url, request.SavePath, request.Category),
            ct).ConfigureAwait(false);

        var torrents = await _qBittorrent.ListTorrentsAsync(ct).ConfigureAwait(false);
        var added = torrents.FirstOrDefault(t => t.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase))
                    ?? torrents.LastOrDefault();

        return new DownloadTicket(
            added?.Hash ?? hash,
            Type,
            added?.Name ?? request.Query ?? "torrent");
    }

    public async Task<DownloadStatus> GetStatusAsync(string downloadId, CancellationToken ct = default)
    {
        var torrents = await _qBittorrent.ListTorrentsAsync(ct).ConfigureAwait(false);
        var torrent = torrents.FirstOrDefault(t => t.Hash.Equals(downloadId, StringComparison.OrdinalIgnoreCase))
                      ?? throw new KeyNotFoundException($"Torrent '{downloadId}' was not found.");

        return new DownloadStatus(
            torrent.Hash,
            Type,
            torrent.Name,
            torrent.Paused ? "paused" : torrent.State,
            torrent.Progress,
            torrent.SizeBytes,
            torrent.DownloadedBytes);
    }

    public Task PauseAsync(string downloadId, CancellationToken ct = default) =>
        _qBittorrent.PauseAsync(downloadId, ct);

    public Task ResumeAsync(string downloadId, CancellationToken ct = default) =>
        _qBittorrent.ResumeAsync(downloadId, ct);

    public Task CancelAsync(string downloadId, CancellationToken ct = default) =>
        _qBittorrent.DeleteAsync(downloadId, deleteFiles: false, ct);

    private string ResolveStartUrl(DownloadStartRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Magnet))
        {
            return request.Magnet;
        }

        if (!string.IsNullOrWhiteSpace(request.Url))
        {
            return request.Url;
        }

        if (request.SearchIndex is >= 0 && request.SearchIndex.Value < _lastSearchResults.Count)
        {
            var selected = _lastSearchResults[request.SearchIndex.Value];
            return !string.IsNullOrWhiteSpace(selected.MagnetUri)
                ? selected.MagnetUri
                : selected.DownloadUrl ?? throw new InvalidOperationException("Selected torrent has no magnet or URL.");
        }

        throw new InvalidOperationException("Torrent start requires magnet, url, or searchIndex.");
    }
}