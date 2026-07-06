using System.Collections.Concurrent;
using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Plugins.Torrent;

public sealed class TorrentSearchSessionStore
{
    private readonly ConcurrentDictionary<string, SearchSession> _sessions = new(StringComparer.Ordinal);

    public void Save(string userId, string query, IReadOnlyList<DownloadSearchResult> results, int page = 0, int pageSize = 5)
    {
        _sessions[userId] = new SearchSession(query, results.ToList(), page, pageSize, DateTimeOffset.UtcNow);
    }

    public bool TryGet(string userId, out SearchSession session) =>
        _sessions.TryGetValue(userId, out session!);

    public void Clear(string userId) => _sessions.TryRemove(userId, out _);

    public IReadOnlyList<DownloadSearchResult> GetPage(string userId, int? page = null)
    {
        if (!_sessions.TryGetValue(userId, out var session))
        {
            return [];
        }

        var effectivePage = page ?? session.Page;
        return session.Results
            .Skip(effectivePage * session.PageSize)
            .Take(session.PageSize)
            .ToList();
    }

    public void SetPage(string userId, int page)
    {
        if (_sessions.TryGetValue(userId, out var session))
        {
            _sessions[userId] = session with { Page = page };
        }
    }

    public sealed record SearchSession(
        string Query,
        List<DownloadSearchResult> Results,
        int Page,
        int PageSize,
        DateTimeOffset CreatedAt);
}