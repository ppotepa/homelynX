using TorrentBot.Integrations.Models;

namespace TorrentBot.Integrations.Interfaces;

public interface IQBittorrentClient
{
    Task<IReadOnlyList<TorrentInfo>> ListTorrentsAsync(CancellationToken ct = default);
    Task<string> AddTorrentAsync(AddTorrentRequest request, CancellationToken ct = default);
    Task PauseAsync(string hash, CancellationToken ct = default);
    Task ResumeAsync(string hash, CancellationToken ct = default);
    Task DeleteAsync(string hash, bool deleteFiles = false, CancellationToken ct = default);
}