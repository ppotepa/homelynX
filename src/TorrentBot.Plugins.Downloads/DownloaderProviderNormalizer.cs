namespace TorrentBot.Plugins.Downloads;

internal static class DownloaderProviderNormalizer
{
    private static readonly HashSet<string> KnownProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "torrent",
        "url"
    };

    /// <summary>
    /// Maps LLM/user provider values to registered downloader types (torrent, url).
    /// Jackett indexer names, "all", empty strings, etc. fall back to torrent search.
    /// </summary>
    public static string Normalize(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return "torrent";
        }

        var trimmed = provider.Trim();
        if (KnownProviders.Contains(trimmed))
        {
            return trimmed.Equals("url", StringComparison.OrdinalIgnoreCase) ? "url" : "torrent";
        }

        return "torrent";
    }
}