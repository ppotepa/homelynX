using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Engine.Pipeline.ResponseArtifacts;

internal static class SearchResultsArtifactParser
{
    public static bool TryParse(
        Dictionary<string, object?> data,
        out SearchResultsArtifact artifact,
        ResponseConstructionSpec spec)
    {
        artifact = null!;
        if (string.IsNullOrWhiteSpace(spec.ItemsKey) || string.IsNullOrWhiteSpace(spec.QueryKey))
        {
            return false;
        }

        if (!data.TryGetValue(spec.ItemsKey, out var resultsRaw)
            || !data.TryGetValue(spec.QueryKey, out var queryRaw))
        {
            return false;
        }

        var query = queryRaw?.ToString() ?? string.Empty;
        var total = data.TryGetValue("totalCount", out var tc) && int.TryParse(tc?.ToString(), out var t) ? t
            : data.TryGetValue("count", out var c) && int.TryParse(c?.ToString(), out var ct) ? ct : 0;
        var page = data.TryGetValue("page", out var p) && int.TryParse(p?.ToString(), out var pg) ? pg : 0;
        var pageSize = data.TryGetValue("pageSize", out var ps) && int.TryParse(ps?.ToString(), out var psz) ? psz : 5;
        var hasMore = data.TryGetValue("hasMore", out var hm) && hm is bool hb ? hb : page * pageSize + pageSize < total;
        var totalPages = data.TryGetValue("totalPages", out var tp) && int.TryParse(tp?.ToString(), out var tpg)
            ? tpg
            : pageSize > 0 ? Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)) : 1;

        var items = new List<SearchResultItem>();
        if (resultsRaw is System.Collections.IEnumerable enumerable)
        {
            foreach (var entry in enumerable)
            {
                if (entry is SearchResultItem item)
                {
                    items.Add(item);
                    continue;
                }

                if (entry is Dictionary<string, object?> dict)
                {
                    items.Add(new SearchResultItem(
                        dict.TryGetValue("index", out var ix) && int.TryParse(ix?.ToString(), out var i) ? i : items.Count + 1,
                        dict.TryGetValue("name", out var n) ? n?.ToString() ?? "?" : "?",
                        dict.TryGetValue("size", out var sz) && long.TryParse(sz?.ToString(), out var size) ? size
                            : dict.TryGetValue("sizeBytes", out var sb) && long.TryParse(sb?.ToString(), out var sbb) ? sbb : 0,
                        dict.TryGetValue("seeders", out var sd) && int.TryParse(sd?.ToString(), out var seeds) ? seeds : null,
                        dict.TryGetValue("magnet", out var m) ? m?.ToString() : dict.TryGetValue("magnetUri", out var mu) ? mu?.ToString() : null,
                        dict.TryGetValue("url", out var u) ? u?.ToString() : null,
                        dict.TryGetValue("provider", out var pr) ? pr?.ToString() ?? "torrent" : "torrent"));
                }
            }
        }

        artifact = new SearchResultsArtifact(query, total, page, pageSize, items, hasMore, Math.Max(1, totalPages));
        return true;
    }
}