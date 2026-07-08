using TorrentBot.Llm.Polish;

namespace TorrentBot.Llm;

public sealed record LlmIntentContext(
    string OriginalText,
    string NormalizedText,
    string? ForcedSearchQuery = null,
    bool IsStatusList = false)
{
    public bool WasNormalized => !string.Equals(OriginalText, NormalizedText, StringComparison.Ordinal);
}

public static class LlmIntentNormalizer
{
    private static readonly string[] SearchKeywords = ["znajdź", "szukaj", "search", "find "];
    private static readonly string[] StatusListKeywords =
    [
        "pokaż pobierania", "pokaz pobierania", "pokaż status", "pokaz status",
        "status pobierania", "stan pobierania", "pokaż torrenty", "pokaz torrenty",
        "status torrenty", "stan torrentow", "list torrents", "pokaż torrenty",
        "co się pobiera", "co pobiera", "pobierania", "torrenty status"
    ];

    private static readonly string[] DownloadPrefixes = ["download ", "pobierz ", "ściągnij ", "sciag ", "get "];
    private static readonly string[] DownloadExclusions =
    [
        "download list", "download status", "lista pobieran", "pokaż pobierania", "pokaz pobierania"
    ];

    public static LlmIntentContext Analyze(string text)
    {
        var original = text ?? string.Empty;
        var normalizedText = PolishLexicon.NormalizeForLlm(original);
        var lower = normalizedText.ToLowerInvariant();
        string? forcedSearchQuery = null;
        var isStatusList = false;

        if (TryExtractDownloadSearchQuery(normalizedText, out var downloadQuery))
        {
            forcedSearchQuery = downloadQuery;
            normalizedText = $"search for {downloadQuery} -- MUST use torrent.search capability with this query";
        }
        else if (SearchKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal))
            && !lower.Contains("status", StringComparison.Ordinal)
            && !lower.Contains("pokaż", StringComparison.Ordinal)
            && !lower.Contains("show", StringComparison.Ordinal))
        {
            var q = ExtractSearchQuery(original, lower);
            forcedSearchQuery = q;
            normalizedText = $"search for {q} -- MUST use torrent.search capability with this query";
        }

        isStatusList = StatusListKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal))
            || (lower.Contains("pokaż", StringComparison.Ordinal)
                && (lower.Contains("pobrani", StringComparison.Ordinal)
                    || lower.Contains("torrent", StringComparison.Ordinal)
                    || lower.Contains("download", StringComparison.Ordinal)
                    || lower.Contains("pobier", StringComparison.Ordinal)));

        if (isStatusList && forcedSearchQuery is null)
        {
            normalizedText = lower.Contains("torrent", StringComparison.Ordinal)
                || lower.Contains("torrenty", StringComparison.Ordinal)
                || lower.Contains("torrents", StringComparison.Ordinal)
                ? "list torrents -- MUST use torrent.list capability for rich status"
                : "show downloads -- MUST use download.list or query.execute source=downloads for rich details (progress, speed, eta)";
        }

        return new LlmIntentContext(original, normalizedText, forcedSearchQuery, isStatusList);
    }

    public static bool TryExtractDownloadSearchQuery(string text, out string query)
    {
        query = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var lower = trimmed.ToLowerInvariant();
        if (DownloadExclusions.Any(e => lower.Contains(e, StringComparison.Ordinal)))
        {
            return false;
        }

        if (lower.Contains(" status", StringComparison.Ordinal) || lower.StartsWith("status ", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var prefix in DownloadPrefixes)
        {
            if (!lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            query = trimmed[prefix.Length..].Trim();
            return query.Length >= 2;
        }

        return false;
    }

    private static string ExtractSearchQuery(string original, string lower)
    {
        if (lower.Contains("znajdź ", StringComparison.Ordinal))
        {
            return original.Substring(original.ToLowerInvariant().IndexOf("znajdź ", StringComparison.Ordinal) + 7).Trim();
        }

        if (lower.Contains("szukaj ", StringComparison.Ordinal))
        {
            return original.Substring(original.ToLowerInvariant().IndexOf("szukaj ", StringComparison.Ordinal) + 6).Trim();
        }

        if (lower.Contains("search for ", StringComparison.Ordinal))
        {
            return original.Substring(original.ToLowerInvariant().IndexOf("search for ", StringComparison.Ordinal) + 11).Trim();
        }

        if (lower.Contains("find ", StringComparison.Ordinal))
        {
            return original.Substring(original.ToLowerInvariant().IndexOf("find ", StringComparison.Ordinal) + 5).Trim();
        }

        if (lower.Contains("search ", StringComparison.Ordinal))
        {
            return original.Substring(original.ToLowerInvariant().IndexOf("search ", StringComparison.Ordinal) + 7).Trim();
        }

        return original;
    }
}