using TorrentBot.Contracts.Artifacts;
using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Plugins.Torrent;

internal static class SearchResultsBuilder
{
    public const int DefaultPageSize = 5;

    public static Dictionary<string, object?> BuildPageData(
        string query,
        IReadOnlyList<DownloadSearchResult> allResults,
        int page,
        int pageSize = DefaultPageSize)
    {
        var total = allResults.Count;
        var totalPages = pageSize > 0 ? Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)) : 1;
        var pageResults = allResults
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();
        var slice = TorrentSearchDisplay.BuildItemRecords(pageResults, page, pageSize);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["artifactKind"] = "search_results",
            ["query"] = query,
            ["totalCount"] = total,
            ["count"] = total,
            ["page"] = page,
            ["pageSize"] = pageSize,
            ["hasMore"] = (page + 1) * pageSize < total,
            ["totalPages"] = totalPages,
            ["results"] = slice
        };
    }

    public static IReadOnlyList<SearchResultItem> ToItems(IReadOnlyList<DownloadSearchResult> page, int pageIndex, int pageSize)
    {
        return TorrentSearchDisplay.BuildItemRecords(page, pageIndex, pageSize)
            .Select(record => new SearchResultItem(
                record.TryGetValue("index", out var ix) && int.TryParse(ix?.ToString(), out var index) ? index : 1,
                record.TryGetValue("name", out var n) ? n?.ToString() ?? "?" : "?",
                record.TryGetValue("sizeBytes", out var sz) && long.TryParse(sz?.ToString(), out var size) ? size : 0,
                record.TryGetValue("seeders", out var sd) && int.TryParse(sd?.ToString(), out var seeds) ? seeds : null,
                record.TryGetValue("magnetUri", out var mu) ? mu?.ToString() : record.TryGetValue("magnet", out var m) ? m?.ToString() : null,
                record.TryGetValue("url", out var u) ? u?.ToString() : null,
                record.TryGetValue("provider", out var pr) ? pr?.ToString() ?? "torrent" : "torrent"))
            .ToList();
    }
}