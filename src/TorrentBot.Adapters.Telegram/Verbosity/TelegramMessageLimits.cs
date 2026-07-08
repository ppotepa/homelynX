namespace TorrentBot.Adapters.Telegram.Verbosity;

public static class TelegramMessageLimits
{
    public const int MaxMessageLength = 4096;

    public static string Truncate(string text, int maxLength = MaxMessageLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        const string suffix = "\n… (truncated)";
        var keep = Math.Max(0, maxLength - suffix.Length);
        return text[..keep] + suffix;
    }
}