using TorrentBot.Contracts.Context;
using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Plugins.Torrent;

/// <summary>
/// Single authority for torrent search session state stored in <see cref="ConversationContext"/> snapshots.
/// </summary>
public static class TorrentSearchConversationState
{
    public const string SnapshotSource = "torrent_search_results";
    public const int DefaultPageSize = 5;

    public sealed record Session(
        string Query,
        IReadOnlyList<DownloadSearchResult> Results,
        int Page,
        int PageSize,
        DateTimeOffset CreatedAt);

    public static void Save(
        ConversationContext context,
        string query,
        IReadOnlyList<DownloadSearchResult> results,
        int page = 0,
        int pageSize = DefaultPageSize)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.UpdateSnapshot(SnapshotSource, BuildSnapshot(query, results, page, pageSize));
    }

    public static bool TryGet(ConversationContext context, out Session session)
    {
        ArgumentNullException.ThrowIfNull(context);
        session = null!;
        if (context.GetSnapshot(SnapshotSource) is not { } snapshot)
        {
            return false;
        }

        if (!snapshot.State.TryGetValue("query", out var queryObj)
            || string.IsNullOrWhiteSpace(queryObj?.ToString()))
        {
            return false;
        }

        var results = DeserializeResults(snapshot.State.TryGetValue("results", out var raw) ? raw : null);
        if (results.Count == 0)
        {
            return false;
        }

        var page = snapshot.State.TryGetValue("page", out var pageObj) && int.TryParse(pageObj?.ToString(), out var p) ? p : 0;
        var pageSize = snapshot.State.TryGetValue("pageSize", out var psObj) && int.TryParse(psObj?.ToString(), out var psz)
            ? psz
            : DefaultPageSize;
        var createdAt = snapshot.State.TryGetValue("createdAt", out var createdObj)
            && DateTimeOffset.TryParse(createdObj?.ToString(), out var created)
            ? created
            : DateTimeOffset.UtcNow;

        session = new Session(queryObj.ToString()!, results, page, pageSize, createdAt);
        return true;
    }

    public static IReadOnlyList<DownloadSearchResult> GetPage(ConversationContext context, int? page = null)
    {
        if (!TryGet(context, out var session))
        {
            return [];
        }

        var effectivePage = page ?? session.Page;
        return session.Results
            .Skip(effectivePage * session.PageSize)
            .Take(session.PageSize)
            .ToList();
    }

    public static void SetPage(ConversationContext context, int page)
    {
        if (!TryGet(context, out var session))
        {
            return;
        }

        Save(context, session.Query, session.Results, page, session.PageSize);
    }

    public static void Clear(ConversationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.RemoveSnapshot(SnapshotSource);
    }

    internal static ContextSnapshot BuildSnapshot(
        string query,
        IReadOnlyList<DownloadSearchResult> results,
        int page,
        int pageSize)
    {
        var pageResults = results
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();
        var items = TorrentSearchDisplay.BuildItemRecords(pageResults, page, pageSize)
            .Cast<object>()
            .ToList();

        return new ContextSnapshot(
            new Dictionary<string, object?>
            {
                ["query"] = query,
                ["page"] = page,
                ["pageSize"] = pageSize,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["results"] = SerializeResults(results),
                ["items"] = items,
                ["count"] = results.Count
            },
            DateTime.UtcNow);
    }

    private static List<Dictionary<string, object?>> SerializeResults(IReadOnlyList<DownloadSearchResult> results) =>
        results.Select(r => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = r.Id,
            ["name"] = r.Name,
            ["provider"] = r.Provider,
            ["sizeBytes"] = r.SizeBytes,
            ["seeders"] = r.Seeders,
            ["magnetUri"] = r.MagnetUri,
            ["url"] = r.Url
        }).ToList();

    private static List<DownloadSearchResult> DeserializeResults(object? raw)
    {
        var results = new List<DownloadSearchResult>();
        if (raw is not System.Collections.IEnumerable enumerable)
        {
            return results;
        }

        foreach (var entry in enumerable)
        {
            if (entry is not Dictionary<string, object?> dict)
            {
                continue;
            }

            results.Add(new DownloadSearchResult(
                dict.TryGetValue("id", out var id) ? id?.ToString() ?? "" : "",
                dict.TryGetValue("name", out var name) ? name?.ToString() ?? "?" : "?",
                dict.TryGetValue("provider", out var provider) ? provider?.ToString() ?? "torrent" : "torrent",
                dict.TryGetValue("sizeBytes", out var size) && long.TryParse(size?.ToString(), out var sz) ? sz : 0,
                dict.TryGetValue("seeders", out var seeders) && int.TryParse(seeders?.ToString(), out var sd) ? sd : 0,
                dict.TryGetValue("magnetUri", out var magnet) ? magnet?.ToString() : null,
                dict.TryGetValue("url", out var url) ? url?.ToString() : null));
        }

        return results;
    }
}