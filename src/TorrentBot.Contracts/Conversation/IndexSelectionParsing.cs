using System.Text.RegularExpressions;

namespace TorrentBot.Contracts.Conversation;

/// <summary>
/// Parses 1-based display indexes from natural-language or slash-command selection utterances.
/// </summary>
public static class IndexSelectionParsing
{
    private static readonly (string[] Keys, int Value)[] Ordinals =
    [
        (["pierwszy", "pierwsza", "pierwsze", "first", "1st"], 1),
        (["drugi", "druga", "second", "2nd"], 2),
        (["trzeci", "trzecia", "third", "3rd"], 3),
        (["czwarty", "czwarta", "fourth", "4th"], 4),
        (["piaty", "piąty", "piata", "piąta", "fifth", "5th"], 5)
    ];

    private static readonly Regex[] NumericPatterns =
    [
        new(@"^(?:/select|select|wybierz|pick|#)\s*(\d+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(?:/select|select|wybierz|pick)\s+(?:the\s+)?(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"^(\d+)$", RegexOptions.CultureInvariant)
    ];

    public static bool TryParseDisplayIndex(string? text, out int index)
    {
        index = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().ToLowerInvariant();
        foreach (var pattern in NumericPatterns)
        {
            var match = pattern.Match(normalized);
            if (match.Success && int.TryParse(match.Groups[1].Value, out index) && index >= 1)
            {
                return true;
            }
        }

        foreach (var (keys, value) in Ordinals)
        {
            if (keys.Any(key => normalized.Contains(key, StringComparison.Ordinal)))
            {
                index = value;
                return true;
            }
        }

        return false;
    }

    public static bool LooksLikeIndexSelection(string? text)
    {
        if (TryParseDisplayIndex(text, out _))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.ToLowerInvariant();
        return normalized.Contains("select", StringComparison.Ordinal)
            || normalized.Contains("wybierz", StringComparison.Ordinal)
            || normalized.Contains("pick", StringComparison.Ordinal);
    }
}