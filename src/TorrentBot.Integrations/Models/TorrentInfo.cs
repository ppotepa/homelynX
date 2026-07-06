namespace TorrentBot.Integrations.Models;

public sealed record TorrentInfo(
    string Hash,
    string Name,
    string State,
    double Progress,
    long SizeBytes,
    long DownloadedBytes,
    double DownloadSpeed,
    double UploadSpeed,
    string Category,
    bool Paused);