namespace TorrentBot.Plugins.Torrent;

/// <summary>
/// Converts 1-based displayed search indexes (UI, /select N) to 0-based storage indexes.
/// </summary>
public static class TorrentSearchIndex
{
    public static bool TryToGlobalIndex(
        int displayedIndex,
        int page,
        int pageSize,
        int totalCount,
        out int globalIndex)
    {
        globalIndex = -1;
        if (displayedIndex < 1)
        {
            return false;
        }

        globalIndex = page * pageSize + (displayedIndex - 1);
        return globalIndex >= 0 && globalIndex < totalCount;
    }
}
