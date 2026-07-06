using System.Collections.Concurrent;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Integrations.Models;

namespace TorrentBot.Integrations.Fakes;

public sealed class FakeQBittorrentClient : IQBittorrentClient
{
    private readonly ConcurrentDictionary<string, TorrentInfo> _torrents = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<TorrentInfo>> ListTorrentsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TorrentInfo>>(_torrents.Values.OrderBy(t => t.Name).ToList());

    public Task<string> AddTorrentAsync(AddTorrentRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UrlOrMagnet);

        var hash = $"fake-{Guid.NewGuid():N}"[..16];
        var name = ExtractName(request.UrlOrMagnet);
        var torrent = new TorrentInfo(
            Hash: hash,
            Name: name,
            State: "downloading",
            Progress: 0,
            SizeBytes: 1_073_741_824,
            DownloadedBytes: 0,
            DownloadSpeed: 0,
            UploadSpeed: 0,
            Category: request.Category ?? string.Empty,
            Paused: false);

        _torrents[hash] = torrent;
        return Task.FromResult(hash);
    }

    public Task PauseAsync(string hash, CancellationToken ct = default)
    {
        Update(hash, t => t with { Paused = true, State = "paused" });
        return Task.CompletedTask;
    }

    public Task ResumeAsync(string hash, CancellationToken ct = default)
    {
        Update(hash, t => t with { Paused = false, State = "downloading" });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string hash, bool deleteFiles = false, CancellationToken ct = default)
    {
        _torrents.TryRemove(hash, out _);
        return Task.CompletedTask;
    }

    private void Update(string hash, Func<TorrentInfo, TorrentInfo> updater)
    {
        if (_torrents.TryGetValue(hash, out var current))
        {
            _torrents[hash] = updater(current);
        }
    }

    private static string ExtractName(string urlOrMagnet)
    {
        if (urlOrMagnet.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            var dnIndex = urlOrMagnet.IndexOf("dn=", StringComparison.OrdinalIgnoreCase);
            if (dnIndex >= 0)
            {
                var start = dnIndex + 3;
                var end = urlOrMagnet.IndexOf('&', start);
                var encoded = end < 0 ? urlOrMagnet[start..] : urlOrMagnet[start..end];
                return Uri.UnescapeDataString(encoded);
            }
        }

        return Path.GetFileName(urlOrMagnet.TrimEnd('/')) is { Length: > 0 } fileName
            ? fileName
            : "torrent";
    }
}