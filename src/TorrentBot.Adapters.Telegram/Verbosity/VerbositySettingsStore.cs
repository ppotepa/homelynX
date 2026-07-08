using System.Collections.Concurrent;

namespace TorrentBot.Adapters.Telegram.Verbosity;

public enum VerbosityLevel
{
    Off,
    Low,
    Medium,
    Full,
    Debug
}

public sealed class VerbositySettingsStore
{
    private readonly ConcurrentDictionary<string, VerbosityLevel> _levels = new(StringComparer.Ordinal);

    public VerbosityLevel Get(long chatId) =>
        _levels.GetValueOrDefault(chatId.ToString(), VerbosityLevel.Medium);

    public void Set(long chatId, VerbosityLevel level) => _levels[chatId.ToString()] = level;

    public static bool TryParse(string? text, out VerbosityLevel level)
    {
        level = VerbosityLevel.Medium;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().ToLowerInvariant();
        if (normalized.StartsWith('/'))
        {
            normalized = normalized.TrimStart('/');
        }

        if (normalized.StartsWith("config ", StringComparison.Ordinal))
        {
            normalized = normalized["config ".Length..];
        }

        if (normalized.StartsWith("verbosity ", StringComparison.Ordinal))
        {
            normalized = normalized["verbosity ".Length..];
        }

        switch (normalized)
        {
            case "off":
                level = VerbosityLevel.Off;
                return true;
            case "low":
                level = VerbosityLevel.Low;
                return true;
            case "medium":
                level = VerbosityLevel.Medium;
                return true;
            case "full":
                level = VerbosityLevel.Full;
                return true;
            case "debug":
            case "debig": // common typo tolerance
                level = VerbosityLevel.Debug;
                return true;
            default:
                // tolerate small typos / partials for debug (very common in logs)
                if (normalized.Contains("debug") || normalized.Contains("debig") || Levenshtein(normalized, "debug") <= 1)
                {
                    level = VerbosityLevel.Debug;
                    return true;
                }
                return false;
        }
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var costs = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) costs[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = costs[0]; costs[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cur = costs[j];
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                costs[j] = Math.Min(Math.Min(costs[j] + 1, costs[j - 1] + 1), prev + cost);
                prev = cur;
            }
        }
        return costs[b.Length];
    }
}