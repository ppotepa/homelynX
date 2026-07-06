namespace TorrentBot.Integrations.Models;

public sealed record TorrentSearchResult(
    string Title,
    string MagnetUri,
    string? DownloadUrl,
    long SizeBytes,
    int Seeders,
    string Indexer,
    string? InfoHash = null);