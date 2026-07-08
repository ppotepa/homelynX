using TorrentBot.Contracts.Presentation;
using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Plugins.Torrent;

/// <summary>
/// Single authority for torrent search display indexes and item record shape.
/// </summary>
public static class TorrentSearchDisplay
{
    public static IReadOnlyList<Dictionary<string, object?>> BuildItemRecords(
        IReadOnlyList<DownloadSearchResult> pageResults,
        int pageIndex,
        int pageSize)
    {
        return pageResults
            .Select((result, offset) => new Dictionary<string, object?>(StringComparer.Ordinal)
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
    }

    public static string FormatPromptLine(IReadOnlyDictionary<string, object?> record) =>
        TorrentSearchPromptFormatting.FormatLine(record);

    public static bool TrySelectGlobalIndex(
        int displayedIndex,
        int page,
        int pageSize,
        int totalCount,
        out int globalIndex) =>
        TorrentSearchIndex.TryToGlobalIndex(displayedIndex, page, pageSize, totalCount, out globalIndex);
}