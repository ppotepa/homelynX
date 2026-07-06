using TorrentBot.Integrations.Models;

namespace TorrentBot.Integrations.Interfaces;

public interface IJackettClient
{
    Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(string query, CancellationToken ct = default);
}