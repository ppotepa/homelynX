namespace TorrentBot.Contracts.Conversation;

public static class YesNoResponseParsing
{
    private static readonly HashSet<string> Confirm = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "y", "tak", "ok", "confirm", "potwierdz", "potwierdź", "approve", "approved"
    };

    private static readonly HashSet<string> Cancel = new(StringComparer.OrdinalIgnoreCase)
    {
        "no", "n", "nie", "cancel", "anuluj", "reject", "rejected", "stop"
    };

    public static bool TryParse(string? text, out string responseType)
    {
        responseType = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var token = text.Trim();
        if (Confirm.Contains(token))
        {
            responseType = "confirm";
            return true;
        }

        if (Cancel.Contains(token))
        {
            responseType = "cancel";
            return true;
        }

        return false;
    }
}