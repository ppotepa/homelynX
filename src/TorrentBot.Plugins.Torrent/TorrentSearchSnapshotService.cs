using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Repositories;
using TorrentBot.Engine.Context;
using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Plugins.Torrent;

/// <summary>
/// Facade over conversation-backed torrent search state for handlers and snapshot queries.
/// </summary>
public sealed class TorrentSearchSnapshotService : ISnapshotSource
{
    private readonly ConversationContextStore _store;

    public TorrentSearchSnapshotService(ConversationContextStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public string Name => TorrentSearchConversationState.SnapshotSource;

    public void Save(string userId, string query, IReadOnlyList<DownloadSearchResult> results, int page = 0, int pageSize = 5)
    {
        var context = _store.GetOrCreate(userId, userId);
        TorrentSearchConversationState.Save(context, query, results, page, pageSize);
    }

    public bool TryGet(string userId, out TorrentSearchConversationState.Session session)
    {
        var context = _store.GetOrCreate(userId, userId);
        return TorrentSearchConversationState.TryGet(context, out session);
    }

    public void Clear(string userId)
    {
        foreach (var context in _store.GetAllForUser(userId))
        {
            TorrentSearchConversationState.Clear(context);
        }
    }

    public IReadOnlyList<DownloadSearchResult> GetPage(string userId, int? page = null)
    {
        var context = _store.GetOrCreate(userId, userId);
        return TorrentSearchConversationState.GetPage(context, page);
    }

    public void SetPage(string userId, int page)
    {
        var context = _store.GetOrCreate(userId, userId);
        TorrentSearchConversationState.SetPage(context, page);
    }

    public QuerySourceMeta GetManifest() => new(
        Name,
        "Last torrent search results (1-based numbered). Use to resolve 'select 1', 'first result' etc.",
        [new QueryFieldMeta("query", "string"), new QueryFieldMeta("results", "array")],
        ExampleQueries: ["show last search results"]);

    public Task<object> GetSnapshotAsync(CancellationToken ct = default)
    {
        TorrentSearchConversationState.Session? latest = null;
        foreach (var context in _store.GetAllContexts())
        {
            if (!TorrentSearchConversationState.TryGet(context, out var session))
            {
                continue;
            }

            if (latest is null || session.CreatedAt > latest.CreatedAt)
            {
                latest = session;
            }
        }

        if (latest is null)
        {
            return Task.FromResult<object>(new List<object>());
        }

        var pageResults = latest.Results
            .Skip(latest.Page * latest.PageSize)
            .Take(latest.PageSize)
            .ToList();
        var summarized = TorrentSearchDisplay.BuildItemRecords(pageResults, latest.Page, latest.PageSize)
            .Cast<object>()
            .ToList();

        return Task.FromResult<object>(summarized);
    }
}
