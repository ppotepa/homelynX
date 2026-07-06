using System.Collections.Concurrent;

namespace TorrentBot.Adapters.Telegram.Verbosity;

public enum VerbosityLevel
{
    Off,
    Low,
    Medium,
    Full
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
            default:
                return false;
        }
    }
}