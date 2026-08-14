namespace TorrentBot.Engine;

/// <summary>
/// Shared slash-command → capability routing for Telegram and CLI adapters.
/// </summary>
public static class SlashCommandRouting
{
    public static IReadOnlyDictionary<string, string> CapabilityOverrides { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["/download_search"] = "torrent.search",
            ["/list"] = "system.help",
            ["/commands"] = "system.help",
        };

    public static string NormalizeCommand(string raw)
    {
        var command = raw.Trim().ToLowerInvariant();
        var at = command.IndexOf('@');
        if (at > 0)
        {
            command = command[..at];
        }

        return command;
    }

    public static string? ResolveCapabilityOverride(string command) =>
        CapabilityOverrides.TryGetValue(NormalizeCommand(command), out var capability)
            ? capability
            : null;

    public static IReadOnlyDictionary<string, object?>? ParseParameters(string command, string? remainder)
    {
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return null;
        }

        var normalized = NormalizeCommand(command);
        return normalized switch
        {
            "/search" or "/torrent_search" or "/download_search" =>
                new Dictionary<string, object?> { ["query"] = remainder.Trim() },
            "/select" => new Dictionary<string, object?>
            {
                ["index"] = int.TryParse(remainder.Trim(), out var index) ? index : remainder.Trim()
            },
            "/download_candidate" => new Dictionary<string, object?>
            {
                ["title"] = remainder.Trim(),
                ["query"] = remainder.Trim()
            },
            "/download" => ParseKeyValuePairs(remainder),
            "/pause" or "/resume" or "/cancel" => ParseControlParameters(remainder),
            "/torrent_pause" or "/torrent_resume" or "/torrent_delete" =>
                new Dictionary<string, object?> { ["hash"] = remainder.Trim() },
            "/job_cancel" => new Dictionary<string, object?>
            {
                ["jobId"] = remainder.Trim(),
                ["id"] = remainder.Trim()
            },
            "/find_large_files" => int.TryParse(remainder.Trim(), out var minMb)
                ? new Dictionary<string, object?> { ["min_mb"] = minMb }
                : new Dictionary<string, object?> { ["text"] = remainder.Trim() },
            _ => new Dictionary<string, object?> { ["text"] = remainder.Trim() }
        };
    }

    private static Dictionary<string, object?> ParseKeyValuePairs(string remainder)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var token in remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = token.IndexOf('=');
            if (idx > 0)
            {
                result[token[..idx]] = token[(idx + 1)..];
            }
            else if (!result.ContainsKey("url") && Uri.TryCreate(token, UriKind.Absolute, out _))
            {
                result["url"] = token;
                result["provider"] = "url";
            }
            else if (!result.ContainsKey("query"))
            {
                result["query"] = token;
            }
        }

        if (!result.ContainsKey("provider"))
        {
            result["provider"] = result.ContainsKey("url") ? "url" : "torrent";
        }

        return result;
    }

    private static Dictionary<string, object?> ParseControlParameters(string remainder)
    {
        if (remainder.StartsWith("job:", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object?> { ["jobId"] = remainder[4..] };
        }

        return new Dictionary<string, object?> { ["id"] = remainder, ["hash"] = remainder };
    }
}
