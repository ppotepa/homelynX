namespace TorrentBot.Contracts.Presentation;

public static class ResponseFormatting
{
    public static string FormatListMessage(string? formatHint, IEnumerable<object?> items, string? header = null)
    {
        return formatHint switch
        {
            "downloads" => FormatDownloadListMessage(items, header ?? "download(s)"),
            "rich_status" => FormatTorrentListMessage(items, header ?? "torrent(s)"),
            _ => FormatDownloadListMessage(items, header ?? "item(s)")
        };
    }

    public static string FormatDownloadListMessage(IEnumerable<object?> items, string header = "download(s)")
    {
        var rows = ExtractRows(items);
        if (rows.Count == 0)
        {
            return "No active downloads.";
        }

        var lines = new List<string> { $"{rows.Count} {header}" };
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var name = Get(row, "name") ?? "?";
            var status = Get(row, "status") ?? "?";
            var progress = Get(row, "progress") ?? "0";
            var speed = FormatSpeed(Get(row, "dlspeed") ?? Get(row, "dl"));
            var eta = FormatEta(Get(row, "eta"));
            lines.Add($"  [{i}] {name} — {status} {progress}% @ {speed}{eta}");
        }

        return string.Join('\n', lines);
    }

    public static string FormatTorrentListMessage(IEnumerable<object?> items, string header = "torrent(s)")
    {
        var rows = ExtractRows(items);
        if (rows.Count == 0)
        {
            return "No torrents in qBittorrent.";
        }

        var lines = new List<string> { $"{rows.Count} {header} in qBittorrent" };
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var name = Get(row, "name") ?? "?";
            var state = Get(row, "state") ?? "?";
            var progress = Get(row, "progress") ?? "0";
            var downloaded = HumanSize(ParseLong(Get(row, "downloadedBytes") ?? Get(row, "downloaded")));
            var size = HumanSize(ParseLong(Get(row, "sizeBytes") ?? Get(row, "size")));
            var speed = FormatSpeed(Get(row, "dlspeed") ?? Get(row, "downloadSpeed"));
            var eta = FormatEtaFromSpeed(
                ParseLong(Get(row, "sizeBytes") ?? Get(row, "size")),
                ParseLong(Get(row, "downloadedBytes") ?? Get(row, "downloaded")),
                ParseDouble(Get(row, "dlspeed") ?? Get(row, "downloadSpeed")));
            lines.Add($"  [{i}] {name} | {state} {progress}% | {downloaded}/{size} @ {speed}{eta}");
        }

        return string.Join('\n', lines);
    }

    public static IReadOnlyList<Dictionary<string, object?>> ExtractRows(object? raw)
    {
        var rows = new List<Dictionary<string, object?>>();
        if (raw is not System.Collections.IEnumerable enumerable)
        {
            return rows;
        }

        foreach (var entry in enumerable)
        {
            if (entry is Dictionary<string, object?> dict)
            {
                rows.Add(dict);
            }
            else if (entry is System.Collections.IDictionary idict)
            {
                var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in idict.Keys)
                {
                    var keyStr = key?.ToString() ?? "";
                    mapped[keyStr] = idict[key!];
                }
                rows.Add(mapped);
            }
        }

        return rows;
    }

    public static string HumanSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0B";
        }

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double value = bytes;
        while (value >= 1024 && order < units.Length - 1)
        {
            order++;
            value /= 1024;
        }

        return $"{value:0.##}{units[order]}";
    }

    public static string FormatSpeed(string? bytesPerSec)
    {
        if (!double.TryParse(bytesPerSec, out var bytes) || bytes <= 0)
        {
            return "0 B/s";
        }

        string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
        var order = 0;
        while (bytes >= 1024 && order < units.Length - 1)
        {
            order++;
            bytes /= 1024;
        }

        return $"{bytes:0.##} {units[order]}";
    }

    public static string FormatEta(string? etaSec)
    {
        if (string.IsNullOrEmpty(etaSec) || !long.TryParse(etaSec, out var seconds) || seconds <= 0)
        {
            return "";
        }

        if (seconds < 60)
        {
            return $" ETA {seconds}s";
        }

        var minutes = seconds / 60;
        var remainder = seconds % 60;
        return remainder > 0 ? $" ETA {minutes}m {remainder}s" : $" ETA {minutes}m";
    }

    public static string FormatEtaFromSpeed(long sizeBytes, long downloadedBytes, double downloadSpeed)
    {
        if (downloadSpeed <= 0 || sizeBytes <= downloadedBytes)
        {
            return "";
        }

        var remaining = sizeBytes - downloadedBytes;
        var etaSec = (long)(remaining / downloadSpeed);
        return FormatEta(etaSec.ToString());
    }

    private static string? Get(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static long ParseLong(string? value) =>
        long.TryParse(value, out var result) ? result : 0;

    private static double ParseDouble(string? value) =>
        double.TryParse(value, out var result) ? result : 0;
}