using System.Text;

namespace TorrentBot.Adapters.Telegram.Verbosity;

public sealed class ProgressMessageFormatter
{
    private readonly List<StageEntry> _entries = [];
    private string? _userText;
    private string? _planIntent;
    private int _planTotalSteps;
    private readonly DateTime _startTime = DateTime.UtcNow;
    private string? _lastContextSummary;
    private string? _fullPromptForDebug;
    private string? _rawLlmResponseForDebug;
    private readonly Dictionary<string, DateTime> _phaseStartTimes = new();
    private readonly List<string> _savedArtifacts = new();
    private readonly object _gate = new();

    public void SetUserText(string text)
    {
        lock (_gate) _userText = text;
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

                case "planning:start":
                    var plannerLabel = detail == "llm" ? "LLM" : "Command";
                    _entries.Add(new StageEntry("planning", $"🧠 Planowanie... ({plannerLabel})"));
                    break;

                case "planning:done":
                    ReplaceEntry("planning", "✅ Zaplanowano");
                    if (detail is not null)
                    {
                        var parts = detail.Split('|', 2);
                        if (int.TryParse(parts[0], out var count)) _planTotalSteps = count;
                        if (parts.Length > 1) _planIntent = parts[1];
                    }
                    break;

                case "context:refresh":
                    _entries.Add(new StageEntry("context", "📊 Ładowanie kontekstu..."));
                    break;

                case "llm:planning":
                    ReplaceEntry("context", "📊 Kontekst załadowany");
                    var planDetail = detail is not null ? $" ({detail.Split('|')[0]} capabilities, scope: {detail.Split('|').Skip(1).FirstOrDefault()})" : "";
                    _entries.Add(new StageEntry("llm_plan", $"🧠 Planowanie z LLM...{planDetail}"));
                    break;

                case "llm:plan_ready":
                    if (detail is not null)
                    {
                        var parts = detail.Split('|');
                        var steps = parts.Length > 0 ? parts[0] : "?";
                        var intent = parts.Length > 1 ? parts[1] : "";
                        var confidence = parts.Length > 2 ? parts[2] : "";
                        _planTotalSteps = int.TryParse(steps, out var s) ? s : 0;
                        _planIntent = intent;
                        ReplaceEntry("llm_plan", $"✅ Plan: {steps} kroków — \"{intent}\" (confidence: {confidence})");
                    }
                    else
                    {
                        ReplaceEntry("llm_plan", "✅ Plan gotowy");
                    }
                    break;

                case "llm:validated":
                    _entries.Add(new StageEntry("llm_validate", "✅ Plan zatwierdzony"));
                    break;

                case "llm:validation_error":
                    _entries.Add(new StageEntry("llm_validate", $"❌ Walidacja planu nieudana: {detail}"));
                    break;

                case "step:start":
                    if (detail is not null)
                    {
                        var parts = detail.Split('|');
                        var stepNum = parts.Length > 0 ? parts[0] : "";
                        var capName = parts.Length > 1 ? parts[1] : "";
                        _entries.Add(new StageEntry($"step_{stepNum}", $"⏳ {stepNum}: {capName}", true));
                    }
                    break;

                case "step:done":
                    if (detail is not null)
                    {
                        var parts = detail.Split('|');
                        var stepNum = parts.Length > 0 ? parts[0] : "";
                        var capName = parts.Length > 1 ? parts[1] : "";
                        var stepDetail = parts.Length > 2 ? FormatStepDetail(parts[2]) : "";
                        var suffix = string.IsNullOrEmpty(stepDetail) ? "" : $" — {stepDetail}";
                        ReplaceEntry($"step_{stepNum}", $"✅ {stepNum}: {capName}{suffix}");
                    }
                    break;

                case "step:skip":
                    if (detail is not null)
                    {
                        var parts = detail.Split('|');
                        var stepNum = parts.Length > 0 ? parts[0] : "";
                        var capName = parts.Length > 1 ? parts[1] : "";
                        _entries.Add(new StageEntry($"step_{stepNum}", $"⏭️ {stepNum}: {capName} (pominięto)"));
                    }
                    break;

                case "step:error":
                    if (detail is not null)
                    {
                        var parts = detail.Split('|', 3);
                        var stepNum = parts.Length > 0 ? parts[0] : "";
                        var capName = parts.Length > 1 ? parts[1] : "";
                        var error = parts.Length > 2 ? parts[2] : "error";
                        ReplaceEntry($"step_{stepNum}", $"❌ {stepNum}: {capName} — {error}");
                    }
                    break;

                case "llm:responding":
                    _entries.Add(new StageEntry("llm_respond", "💬 Generowanie odpowiedzi..."));
                    break;

                case "llm:responded":
                    ReplaceEntry("llm_respond", "💬 Odpowiedź gotowa");
                    break;

                case "plan":
                    _entries.Add(new StageEntry("plan", $"📝 Plan: {detail}"));
                    break;

                case "execute":
                    _entries.Add(new StageEntry("execute", $"⚙️ Wykonuję: {detail}"));
                    break;

                case "respond":
                    if (detail == "ok")
                        _entries.Add(new StageEntry("respond", "✅ Zakończono"));
                    else
                        _entries.Add(new StageEntry("respond", $"❌ Błąd: {detail}"));
                    break;

                case "confirm":
                    _entries.Add(new StageEntry("confirm", detail == "confirmed" ? "✅ Potwierdzono" : "❌ Odrzucono"));
                    break;

                // Debug events
                case "debug:llm:prompt":
                    if (detail is not null)
                    {
                        var parts = detail.Split('|');
                        var promptLen = parts.Length > 0 ? parts[0] : "?";
                        var capCount = parts.Length > 1 ? parts[1] : "?";
                        var scope = parts.Length > 2 ? parts[2] : "?";
                        var ctx = parts.Length > 3 ? parts[3] : "";
                        _entries.Add(new StageEntry("debug_prompt", $"🔍 Prompt: {promptLen} chars, {capCount} caps, scope:{scope} {ctx}"));
                    }
                    break;

                case "debug:llm:response":
                    if (detail is not null)
                    {
                        var parts = detail.Split('|');
                        var timeMs = parts.Length > 0 ? parts[0] : "?";
                        var respLen = parts.Length > 1 ? parts[1] : "?";
                        var status = parts.Length > 2 ? parts[2] : "?";
                        _entries.Add(new StageEntry("debug_response", $"📥 Response: {timeMs}ms, {respLen} chars ({status})"));
                    }
                    break;

                case "debug:llm:parse_failed":
                    _entries.Add(new StageEntry("debug_parse", "⚠️ Parse failed — LLM returned invalid JSON"));
                    break;

                case "debug:llm:repair":
                    _entries.Add(new StageEntry("debug_repair", $"🔧 Repair: {detail}"));
                    break;

                case "debug:context:loaded":
                    _lastContextSummary = detail;
                    _entries.Add(new StageEntry("debug_context", $"📦 Context: {detail}"));
                    break;

                case "debug:llm:normalized":
                    _entries.Add(new StageEntry("debug_norm", $"🔄 Normalized: {detail}"));
                    break;

                case "debug:request":
                    _entries.Add(new StageEntry("debug_req", $"🆔 {detail}"));
                    break;

                case "debug:llm:full_prompt":
                    _fullPromptForDebug = detail;
                    // Add a summary line, full will be in spoiler at the end
                    _entries.Add(new StageEntry("debug_full_prompt", "🔍 Full prompt available (see below in spoiler)"));
                    break;

                case "debug:llm:raw_response":
                    _rawLlmResponseForDebug = detail;
                    _entries.Add(new StageEntry("debug_raw_response", "🔍 Raw LLM response (tap to expand)"));
                    break;

                case "debug:context:sample":
                    _entries.Add(new StageEntry("debug_ctx_sample", $"📋 Context sample: {detail}"));
                    break;

                case "debug:history:recent":
                    _entries.Add(new StageEntry("debug_history", $"💬 Recent history: {detail}"));
                    break;

                case "debug:invocation:start":
                    _entries.Add(new StageEntry("debug_inv_start", $"🚀 Invocation start: {detail}"));
                    break;

                case "debug:pipeline:complete":
                    _entries.Add(new StageEntry("debug_pipeline_end", $"🏁 Pipeline done: {detail}"));
                    break;

                case "debug:capability:about_to_execute":
                    _entries.Add(new StageEntry("debug_cap", $"⚙️ About to run capability: {detail}"));
                    break;

                case "debug:step:params":
                    _entries.Add(new StageEntry("debug_step_params", $"   📤 Params: {detail}"));
                    break;

                case "debug:step:result":
                    _entries.Add(new StageEntry("debug_step_result", $"   📥 Result: {detail}"));
                    break;

                case "debug:saved":
                    _savedArtifacts.Add(detail ?? "");
                    _entries.Add(new StageEntry("debug_saved", $"💾 Saved as {detail}"));
                    break;

                case "debug:phase:start":
                    if (detail != null)
                    {
                        _phaseStartTimes[detail] = DateTime.UtcNow;
                        _entries.Add(new StageEntry($"phase_{detail}", $"▶️ Phase start: {detail}"));
                    }
                    break;

                case "debug:phase:end":
                    if (detail != null)
                    {
                        var parts = detail.Split('|');
                        var phase = parts[0];
                        var start = _phaseStartTimes.GetValueOrDefault(phase, DateTime.UtcNow);
                        var ms = (DateTime.UtcNow - start).TotalMilliseconds;
                        _entries.Add(new StageEntry($"phase_{phase}_end", $"⏹️ Phase end: {phase} ({ms:F0}ms)"));
                    }
                    break;

                case "debug:plan:details":
                    _entries.Add(new StageEntry("debug_plan_details", $"📋 Plan details: {detail}"));
                    break;
            }
        }
    }

    public string Format()
    {
        lock (_gate)
        {
            var sb = new StringBuilder();
            var elapsedTotal = (DateTime.UtcNow - _startTime).TotalMilliseconds;

            sb.AppendLine("╔════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║  🔍 VERBOSE DEBUG MODE - Full Pipeline Trace               ║");
            sb.AppendLine($"║  Started: {_startTime:yyyy-MM-dd HH:mm:ss.fff}Z | Total so far: {elapsedTotal:F0}ms ║");
            sb.AppendLine("╚════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            // === INPUT & CONTEXT ===
            sb.AppendLine("┌─ INPUT & CONTEXT ─────────────────────────────────────────┐");
            if (!string.IsNullOrEmpty(_userText))
            {
                sb.AppendLine($"│ 📝 User: {_Truncate(_userText, 70)}");
            }
            if (!string.IsNullOrEmpty(_lastContextSummary))
            {
                sb.AppendLine($"│ 📦 {_lastContextSummary}");
            }

            DateTime lastTime = _startTime;
            foreach (var entry in _entries.Where(e => 
                e.Key.StartsWith("debug_inv") || e.Key.StartsWith("debug_req") || 
                e.Key.StartsWith("debug_context") || e.Key.StartsWith("debug_history") || 
                e.Key.StartsWith("debug_norm") || e.Key.StartsWith("debug_ctx_sample")))
            {
                var now = DateTime.UtcNow;
                var delta = (now - lastTime).TotalMilliseconds;
                lastTime = now;
                sb.AppendLine($"│    {entry.Text} (+{delta:F0}ms)");
            }
            sb.AppendLine("└────────────────────────────────────────────────────────────┘");
            sb.AppendLine();

            // === PLANNING ===
            sb.AppendLine("┌─ PLANNING ─────────────────────────────────────────────────┐");
            if (!string.IsNullOrEmpty(_planIntent) && _planTotalSteps > 0)
            {
                sb.AppendLine($"│ 🎯 Intent: {_Truncate(_planIntent, 55)} ({_planTotalSteps} steps)");
            }

            foreach (var entry in _entries.Where(e => 
                e.Key.Contains("planning") || e.Key.Contains("llm_plan") || 
                e.Key.Contains("llm_validate") || e.Key.StartsWith("debug_prompt") || 
                e.Key.StartsWith("debug_raw_response") || e.Key.StartsWith("debug_plan")))
            {
                sb.AppendLine($"│ {entry.Text}");
            }
            sb.AppendLine("└────────────────────────────────────────────────────────────┘");
            sb.AppendLine();

            // === EXECUTION ===
            sb.AppendLine("┌─ EXECUTION ────────────────────────────────────────────────┐");
            foreach (var entry in _entries.Where(e => 
                e.Key.StartsWith("step") || e.Key.StartsWith("debug_cap") || 
                e.Key.StartsWith("debug_step") || e.Key.Contains("pipeline") || 
                e.Key.StartsWith("debug_saved")))
            {
                sb.AppendLine($"│ {entry.Text}");
            }

            if (_savedArtifacts.Count > 0)
            {
                sb.AppendLine($"│ 💾 Saved artifacts: {string.Join(", ", _savedArtifacts)}");
            }
            sb.AppendLine("└────────────────────────────────────────────────────────────┘");
            sb.AppendLine();

            var running = _entries.FirstOrDefault(e => e.IsRunning);
            if (running is not null)
            {
                sb.AppendLine($"⏱️  Currently running: {running.Text}... ({elapsedTotal:F0}ms elapsed)");
            }
            else if (_entries.Count > 0)
            {
                sb.AppendLine($"⏱️  Total elapsed: {elapsedTotal:F0}ms");
            }

            // === DEBUG ARTIFACTS (SPOILERS) ===
            sb.AppendLine();
            sb.AppendLine("┌─ DEBUG ARTIFACTS (expand for full data) ────────────────────┐");

            if (!string.IsNullOrEmpty(_fullPromptForDebug))
            {
                sb.AppendLine("│ 🔍 FULL PROMPT:");
                sb.AppendLine("│ ||");
                sb.AppendLine("│ " + _Truncate(_fullPromptForDebug, 4096).Replace("\n", "\n│ "));
                sb.AppendLine("│ ||");
                sb.AppendLine($"│ (length: ~{_fullPromptForDebug.Length} chars)");
            }

            if (!string.IsNullOrEmpty(_rawLlmResponseForDebug))
            {
                sb.AppendLine("│ 🔍 RAW LLM RESPONSE (critical for plan debugging):");
                sb.AppendLine("│ ||");
                sb.AppendLine("│ " + _Truncate(_rawLlmResponseForDebug, 4096).Replace("\n", "\n│ "));
                sb.AppendLine("│ ||");
            }

            sb.AppendLine("└────────────────────────────────────────────────────────────┘");

            return sb.ToString().TrimEnd();
        }
    }

    private static string FormatStepDetail(string detail)
    {
        if (string.IsNullOrEmpty(detail)) return "";

        if (detail.StartsWith("count="))
            return $"{detail["count=".Length..]} wyników";
        if (detail.StartsWith("jobId="))
            return $"job: {detail["jobId=".Length..].Substring(0, Math.Min(12, detail.Length - "jobId=".Length))}";
        if (detail == "confirmation_required")
            return "wymaga potwierdzenia";

        return detail;
    }

    private void ReplaceEntry(string key, string newText)
    {
        var idx = _entries.FindIndex(e => e.Key == key);
        if (idx >= 0)
        {
            _entries[idx] = new StageEntry(key, newText);
        }
        else
        {
            _entries.Add(new StageEntry(key, newText));
        }
    }

    private static string _Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private sealed record StageEntry(string Key, string Text, bool IsRunning = false);
}
