namespace TorrentBot.Contracts.Artifacts;

public sealed record SearchResultItem(
    int Index,
    string Name,
    long SizeBytes,
    int? Seeders,
    string? MagnetUri,
    string? Url,
    string Provider = "torrent");