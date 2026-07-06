using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;

namespace TorrentBot.Engine.Pipeline;

public static class ArtifactAccumulator
{
    public static ExecutionArtifacts FromExecutionResult(ExecutionResult result)
    {
        var items = new List<IExecutionArtifact>();
        if (!result.Success)
        {
            var code = ResolveErrorCode(result.Error);
            items.Add(new ErrorArtifact(code, result.Error ?? "Failed", ResolveCapability(result)));
            return new ExecutionArtifacts(false, items, result, result.Error);
        }

        if (result.CapabilityResult?.Data is Dictionary<string, object?> data)
        {
            if (TryBuildSearchResults(data, out var search))
            {
                items.Add(search);
            }
            else if (TryBuildConfirmation(data, result, out var confirm))
            {
                items.Add(confirm);
            }
            else if (TryBuildDownloadStarted(data, result, out var download))
            {
                items.Add(download);
            }
        }

        if (items.Count == 0)
        {
            items.Add(new TextArtifact(
                result.CapabilityResult?.Message ?? "OK",
                result.CapabilityResult?.Data));
        }

        return new ExecutionArtifacts(true, items, result);
    }

    private static bool TryBuildSearchResults(Dictionary<string, object?> data, out SearchResultsArtifact artifact)
    {
        artifact = null!;
        if (!data.TryGetValue("artifactKind", out var kind) || kind?.ToString() != "search_results")
        {
            if (!data.ContainsKey("results") || !data.ContainsKey("query"))
            {
                return false;
            }
        }

        var query = data.TryGetValue("query", out var q) ? q?.ToString() ?? string.Empty : string.Empty;
        var total = data.TryGetValue("totalCount", out var tc) && int.TryParse(tc?.ToString(), out var t) ? t
            : data.TryGetValue("count", out var c) && int.TryParse(c?.ToString(), out var ct) ? ct : 0;
        var page = data.TryGetValue("page", out var p) && int.TryParse(p?.ToString(), out var pg) ? pg : 0;
        var pageSize = data.TryGetValue("pageSize", out var ps) && int.TryParse(ps?.ToString(), out var psz) ? psz : 5;
        var hasMore = data.TryGetValue("hasMore", out var hm) && hm is bool hb ? hb : page * pageSize + pageSize < total;
        var totalPages = pageSize > 0 ? (int)Math.Ceiling(total / (double)pageSize) : 1;

        var items = new List<SearchResultItem>();
        if (data.TryGetValue("results", out var results) && results is System.Collections.IEnumerable enumerable)
        {
            var idx = 0;
            foreach (var entry in enumerable)
            {
                if (entry is SearchResultItem item)
                {
                    items.Add(item);
                    idx++;
                    continue;
                }

                if (entry is Dictionary<string, object?> dict)
                {
                    items.Add(new SearchResultItem(
                        dict.TryGetValue("index", out var ix) && int.TryParse(ix?.ToString(), out var i) ? i : idx + 1,
                        dict.TryGetValue("name", out var n) ? n?.ToString() ?? "?" : "?",
                        dict.TryGetValue("size", out var sz) && long.TryParse(sz?.ToString(), out var size) ? size
                            : dict.TryGetValue("sizeBytes", out var sb) && long.TryParse(sb?.ToString(), out var sbb) ? sbb : 0,
                        dict.TryGetValue("seeders", out var sd) && int.TryParse(sd?.ToString(), out var seeds) ? seeds : null,
                        dict.TryGetValue("magnet", out var m) ? m?.ToString() : dict.TryGetValue("magnetUri", out var mu) ? mu?.ToString() : null,
                        dict.TryGetValue("url", out var u) ? u?.ToString() : null,
                        dict.TryGetValue("provider", out var pr) ? pr?.ToString() ?? "torrent" : "torrent"));
                    idx++;
                }
            }
        }

        if (items.Count == 0 && total == 0)
        {
            return false;
        }

        artifact = new SearchResultsArtifact(query, total, page, pageSize, items, hasMore, Math.Max(1, totalPages));
        return true;
    }

    private static bool TryBuildConfirmation(Dictionary<string, object?> data, ExecutionResult result, out ConfirmationArtifact artifact)
    {
        artifact = null!;
        if (!data.TryGetValue("confirmationRequired", out var required)
            || required is not bool needs || !needs
            || !data.TryGetValue("confirmationToken", out var token))
        {
            return false;
        }

        artifact = new ConfirmationArtifact(
            ResolveCapability(result) ?? "unknown",
            token?.ToString() ?? string.Empty,
            result.CapabilityResult?.Message ?? result.Error ?? "Confirmation required.");
        return true;
    }

    private static bool TryBuildDownloadStarted(Dictionary<string, object?> data, ExecutionResult result, out DownloadStartedArtifact artifact)
    {
        artifact = null!;
        if (!data.TryGetValue("artifactKind", out var kind) || kind?.ToString() != "download_started")
        {
            if (!data.ContainsKey("selected") && !data.ContainsKey("ticket") && !data.ContainsKey("processJobId"))
            {
                return false;
            }
        }

        var name = "download";
        if (data.TryGetValue("selected", out var selected) && selected is Dictionary<string, object?> sel
            && sel.TryGetValue("name", out var sn))
        {
            name = sn?.ToString() ?? name;
        }

        var provider = data.TryGetValue("provider", out var pr) ? pr?.ToString() ?? "torrent" : "torrent";
        var jobId = result.CapabilityResult?.JobId
            ?? (data.TryGetValue("jobId", out var j) ? j?.ToString() : null);
        string? downloadId = null;
        if (data.TryGetValue("ticket", out var ticket) && ticket is Dictionary<string, object?> tk
            && tk.TryGetValue("downloadId", out var did))
        {
            downloadId = did?.ToString();
        }
        else if (data.TryGetValue("processJobId", out var pj))
        {
            downloadId = pj?.ToString();
        }

        artifact = new DownloadStartedArtifact(name, provider, jobId, downloadId);
        return true;
    }

    private static string ResolveErrorCode(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "failed";
        }

        if (error.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            return "acl_denied";
        }

        if (error.Contains("not resolved", StringComparison.OrdinalIgnoreCase)
            || error.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "not_found";
        }

        if (error.Contains("Confirmation required", StringComparison.OrdinalIgnoreCase))
        {
            return "confirmation_required";
        }

        return "failed";
    }

    private static string? ResolveCapability(ExecutionResult result) =>
        result.CapabilityResult?.Data is Dictionary<string, object?> data
        && data.TryGetValue("capabilityName", out var name)
            ? name?.ToString()
            : null;
}