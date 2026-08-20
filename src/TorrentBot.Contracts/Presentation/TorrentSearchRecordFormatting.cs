namespace TorrentBot.Contracts.Presentation;

/// <summary>
/// Canonical line formatter for torrent search item records.
/// Records must be dicts produced by <c>TorrentSearchDisplay.BuildItemRecords</c>.
/// </summary>
public static class TorrentSearchRecordFormatting
{
    public static string FormatLine(IReadOnlyDictionary<string, object?> record)
    {
        var index = record.TryGetValue("index", out var ix) ? ix?.ToString() ?? "?" : "?";
        var name = record.TryGetValue("name", out var n) ? n?.ToString() ?? "?" : "?";
        var size = record.TryGetValue("sizeBytes", out var sz) && long.TryParse(sz?.ToString(), out var bytes)
            ? bytes
            : 0;
        var seeders = record.TryGetValue("seeders", out var sd) ? sd?.ToString() ?? "?" : "?";
        return $"[{index}] {name} | {size}B | seeds={seeders}";
    }
}
