using System.Text.Json;

namespace TorrentBot.Llm;

public sealed class LlmAuditLogger
{
    private readonly string _logDirectory;
    private readonly object _gate = new();

    public LlmAuditLogger(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(Path.GetTempPath(), "homelynx-llm-audit");
        Directory.CreateDirectory(_logDirectory);
    }

    public void LogPrompt(string role, string prompt, int capabilitiesCount, string? scope)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
            role,
            prompt_length = prompt.Length,
            capabilities_count = capabilitiesCount,
            scope,
            prompt_preview = prompt.Length > 2000 ? prompt[..2000] + "..." : prompt
        };
        WriteEntry("prompt", entry);

        // Persist the COMPLETE prompt so it can be manually inspected / replayed against Ollama.
        // This is critical for prompt engineering and debugging empty-plan issues.
        LogFullPrompt(role, prompt);
    }

    public void LogFullPrompt(string role, string prompt, string? hint = null)
    {
        lock (_gate)
        {
            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var ts = DateTime.UtcNow.ToString("HHmmss");
            var safeHint = string.IsNullOrWhiteSpace(hint)
                ? ""
                : "_" + new string(hint.Where(char.IsLetterOrDigit).Take(30).ToArray());
            var fileName = $"{date}_{role}_full_{ts}{safeHint}.txt";
            var path = Path.Combine(_logDirectory, fileName);
            File.WriteAllText(path, prompt);
        }
    }

    public void LogResponse(string role, string? response, long elapsedMs, string? extra = null)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
            role,
            elapsed_ms = elapsedMs,
            response_length = response?.Length ?? 0,
            response_preview = response is not null && response.Length > 1000 ? response[..1000] + "..." : response,
            extra
        };
        WriteEntry("response", entry);
    }

    public void LogPlan(string role, string? intent, int stepsCount, double confidence)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
            role,
            intent,
            steps_count = stepsCount,
            confidence
        };
        WriteEntry("plan", entry);
    }

    private void WriteEntry(string type, object entry)
    {
        lock (_gate)
        {
            var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var file = Path.Combine(_logDirectory, $"{date}_{type}.jsonl");
            var json = JsonSerializer.Serialize(entry);
            File.AppendAllText(file, json + Environment.NewLine);
        }
    }
}
