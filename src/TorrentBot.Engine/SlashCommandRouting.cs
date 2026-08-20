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
            ["/disk"] = "system.disk_usage",
            ["/time"] = "tools.time",
            ["/service"] = "tools.services",
            ["/download_media"] = "download.start_media",
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
            "/download_media" => ParseMediaParameters(remainder),
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
            "/note" or "/todo" or "/remind" or "/reminders" or "/timer" or "/timers"
                or "/poll" or "/choose" or "/dice" or "/paste" or "/calc" or "/convert"
                or "/password" or "/passphrase" or "/hash" or "/uuid" or "/base64" or "/slug"
                or "/date" or "/timestamp" or "/weather" or "/rate" or "/qr" or "/barcode" or "/shorten" or "/url" or "/json" or "/urlencode" or "/color" or "/text_stats" or "/base" or "/mediainfo" or "/thumbnail" or "/extract_audio" or "/gif" or "/compress" or "/chiptune" or "/read" or "/screenshot" or "/track" or "/home" or "/location" or "/distance" or "/map" or "/translate" or "/summarize" or "/rewrite" or "/extract_tasks"
                or "/files" or "/trash" or "/service_logs" or "/network" or "/services" or "/webhook" =>
                new Dictionary<string, object?> { ["text"] = remainder.Trim() },
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
                result["provider"] = "torrent";
            }
            else if (!result.ContainsKey("query"))
            {
                result["query"] = token;
            }
        }

        if (!result.ContainsKey("provider"))
        {
            result["provider"] = "torrent";
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

    private static Dictionary<string, object?> ParseMediaParameters(string remainder)
    {
        var tokens = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (tokens.Length > 0 && Uri.TryCreate(tokens[0], UriKind.Absolute, out _))
        {
            result["url"] = tokens[0];
            result["provider"] = "media";
        }

        foreach (var token in tokens.Skip(1))
        {
            var raw = token.Trim();
            var separator = raw.IndexOf('=');
            var key = separator > 0 ? raw[..separator].Trim().ToLowerInvariant() : string.Empty;
            var value = (separator > 0 ? raw[(separator + 1)..] : raw).Trim().ToLowerInvariant();
            if ((key is "format" or "type")
                && (value is "mp3" or "mp4" or "subtitles" or "subs"))
            {
                result["format"] = value is "subs" ? "subtitles" : value;
            }
            else if (value is "mp3" or "mp4")
            {
                result["format"] = value;
            }
            else if (key is "quality" or "bitrate" or "height")
            {
                result["quality"] = value.TrimEnd('k', 'p');
            }
            else if ((value.EndsWith('k') || value.EndsWith('p'))
                && int.TryParse(value[..^1], out _))
            {
                result["quality"] = value[..^1];
            }
            else if (int.TryParse(value, out _))
            {
                result["quality"] = value;
            }
        }

        if (TryParseClip(remainder, out var clipStart, out var clipEnd))
        {
            result["clipStart"] = clipStart;
            result["clipEnd"] = clipEnd;
        }

        var subtitlesAt = Array.FindIndex(tokens, token => token.Equals("subtitles", StringComparison.OrdinalIgnoreCase)
            || token.Equals("subs", StringComparison.OrdinalIgnoreCase));
        if (subtitlesAt >= 0)
        {
            var languages = tokens[(subtitlesAt + 1)..]
                .Where(token => System.Text.RegularExpressions.Regex.IsMatch(token, "^(?:[a-z]{2,3}(?:-[a-z]+)?|all|auto)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .Select(token => token.ToLowerInvariant())
                .ToArray();
            result["subtitles"] = string.Join(',', languages.DefaultIfEmpty("all"));
            if (!result.ContainsKey("format"))
            {
                result["format"] = "subtitles";
            }
        }

        return result;
    }

    public static bool TryParseClip(string text, out string start, out string end)
    {
        start = string.Empty;
        end = string.Empty;
        var patterns = new[]
        {
            @"\bclip\s*\(\s*(?<start>\d{1,2}(?::\d{2}){0,2})\s*,\s*(?<end>\d{1,2}(?::\d{2}){0,2})\s*\)",
            @"\bclip\s*=\s*(?<start>\d{1,2}(?::\d{2}){0,2})\s*[,\-]\s*(?<end>\d{1,2}(?::\d{2}){0,2})",
            @"\bclip\s+(?<start>\d{1,2}(?::\d{2}){0,2})\s+(?<end>\d{1,2}(?::\d{2}){0,2})",
            @"(?<start>\d{1,2}(?::\d{2}){0,2})\s*-\s*(?<end>\d{1,2}(?::\d{2}){0,2})"
        };
        var match = patterns
            .Select(pattern => System.Text.RegularExpressions.Regex.Match(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .FirstOrDefault(candidate => candidate.Success);
        if (match is null)
        {
            return false;
        }

        start = match.Groups["start"].Value;
        end = match.Groups["end"].Value;
        return true;
    }
}
