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
        var slice = allResults
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select((result, offset) => new Dictionary<string, object?>
            {
                ["index"] = offset + 1,
                ["name"] = result.Name,
                ["sizeBytes"] = result.SizeBytes,
                ["seeders"] = result.Seeders,
                ["magnet"] = result.MagnetUri,
                ["magnetUri"] = result.MagnetUri,
                ["url"] = result.Url,
                ["provider"] = result.Provider,
                ["id"] = result.Id
            })
            .ToList();

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
        return page
            .Select((result, offset) => new SearchResultItem(
                pageIndex * pageSize + offset + 1,
                result.Name,
                result.SizeBytes,
                result.Seeders,
                result.MagnetUri,
                result.Url,
                result.Provider))
            .ToList();
    }
}