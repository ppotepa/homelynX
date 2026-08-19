using System.Text;

namespace TorrentBot.Adapters.Telegram.Verbosity;

public sealed class ProgressMessageFormatter
{
    private readonly List<string> _entries = [];
    private readonly object _gate = new();
    private string? _userText;
    private string? _heartbeat;

    public void SetUserText(string text)
    {
        lock (_gate)
        {
            _userText = text;
        }
    }

    public void HandleStage(string stage, string? detail)
    {
        lock (_gate)
        {
            switch (stage)
            {
                case "parse":
                    _userText = detail;
                    break;
                case "command:start":
                    _entries.Add($"Wykonuję: {detail}");
                    break;
                case "command:done":
                    _entries.Add($"Zakończono: {detail}");
                    break;
                case "command:error":
                    _entries.Add($"Błąd: {detail}");
                    break;
                case "confirm":
                    _entries.Add(detail == "confirmed" ? "Potwierdzono." : "Odrzucono.");
                    break;
                case "heartbeat":
                    _heartbeat = detail;
                    break;
            }
        }
    }

    public string Format(bool includeDebugArtifacts = true) =>
        Format(includeDebugArtifacts, TelegramMessageLimits.MaxMessageLength);

    public string Format(bool includeDebugArtifacts, int maxLength)
    {
        lock (_gate)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(_userText))
            {
                sb.AppendLine($"Komenda: {_userText}");
            }

            if (!string.IsNullOrWhiteSpace(_heartbeat))
            {
                sb.AppendLine($"⏳ {_heartbeat}");
            }

            foreach (var entry in _entries)
            {
                sb.AppendLine(entry);
            }

            var value = sb.ToString().TrimEnd();
            if (value.Length <= maxLength)
            {
                return value;
            }

            return maxLength <= 1 ? value[..maxLength] : value[..(maxLength - 1)] + "…";
        }
    }
}
