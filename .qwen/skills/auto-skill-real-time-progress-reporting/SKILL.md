---
name: real-time-progress-reporting
description: Event-driven architecture for real-time Telegram message updates during pipeline execution with verbosity:full
source: auto-skill
extracted_at: '2026-07-06T21:42:58.644Z'
---

# Real-Time Progress Reporting Pattern

## Problem

Long-running operations (LLM planning, multi-step execution) show "Working..." with no feedback for 5-15 seconds. Users don't know what's happening.

## Solution: Event-Driven Progress with Per-Invocation Recorder

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ TelegramProductionAdapter (message lifecycle)               │
│  - Creates per-invocation recorder for verbosity:full       │
│  - Subscribes to OnStage events                             │
│  - Edits message via ProgressThrottler (1 edit/sec)         │
└─────────────────────────────────────────────────────────────┘
                            ↑
                            │ OnStage callback
                            │
┌─────────────────────────────────────────────────────────────┐
│ VerbosityStageRecorder (per-invocation instance)            │
│  - Implements IProgressReporter                             │
│  - Emits OnStage event on each Report() call                │
│  - Accumulates stages for final verbose response            │
└─────────────────────────────────────────────────────────────┘
                            ↑
                            │ IProgressReporter
                            │
┌─────────────────────────────────────────────────────────────┐
│ InvocationPipeline + EngineHost                             │
│  - Calls ProgressReporter.Report(stage, detail)             │
│  - Doesn't know who listens (separation of concerns)        │
└─────────────────────────────────────────────────────────────┘
```

### Key Components

#### 1. IProgressReporter Interface

```csharp
// TorrentBot.Contracts/Pipeline/IProgressReporter.cs
public interface IProgressReporter
{
    void Report(string stage, string? detail = null);
}
```

Added to `Invocation` so pipeline can emit progress:
```csharp
public sealed class Invocation
{
    // ... existing properties
    public IProgressReporter? ProgressReporter { get; init; }
}
```

#### 2. VerbosityStageRecorder with OnStage Event

```csharp
public sealed class VerbosityStageRecorder : IDisposable, IProgressReporter
{
    private readonly List<VerbosityStageMessage> _stages = [];
    
    public event Action<VerbosityStageMessage>? OnStage;
    
    public void Record(string stage, string? detail = null, ...)
    {
        var message = new VerbosityStageMessage { Stage = stage, Detail = detail, ... };
        lock (_stages) { _stages.Add(message); }
        OnStage?.Invoke(message);  // Notify subscribers
    }
    
    void IProgressReporter.Report(string stage, string? detail) => Record(stage, detail);
}
```

**Key:** Constructor takes optional `IEngine?` — per-invocation recorders don't need engine subscription.

#### 3. Progress Points in Pipeline

**InvocationPipeline.RunAsync:**
```csharp
progress?.Report("planning:start", plannerKind);  // "llm" or "deterministic"
var plan = await planner.PlanAsync(...);
progress?.Report("planning:done", $"{plan.Steps.Count}|{plan.Intent}");

foreach (var step in plan.Steps)
{
    progress?.Report("step:start", $"{stepIndex}/{totalSteps}|{step.CapabilityName}");
    var result = await _engine.SubmitAsync(...);
    var detail = ExtractStepDetail(result);  // "count=221", "jobId=abc", etc.
    progress?.Report("step:done", $"{stepIndex}/{totalSteps}|{step.CapabilityName}|{detail}");
}
```

**EngineHost.HandleNaturalLanguageAsync:**
```csharp
progress?.Report("context:refresh", null);
await RefreshSnapshotsAsync(...);

progress?.Report("llm:planning", $"{allowed.Count}|{scope}");
var llmResult = await _options.LlmPipeline.RunAsync(...);
progress?.Report("llm:plan_ready", $"{steps}|{intent}|{confidence}");

progress?.Report("llm:validated", null);

foreach (var step in steps)
{
    progress?.Report("step:start", $"{i}/{total}|{capability}");
    var result = await ExecuteCapabilityAsync(...);
    progress?.Report("step:done", $"{i}/{total}|{capability}|{detail}");
}

progress?.Report("llm:responding", null);
var reply = await ComposeReply(...);
progress?.Report("llm:responded", null);
```

#### 4. ProgressThrottler

Prevents Telegram API rate limits (30 edits/sec max, but UX-wise 1/sec is enough):

```csharp
public sealed class ProgressThrottler : IDisposable
{
    private readonly TimeSpan _minInterval;  // 1 second
    private DateTimeOffset _lastEdit = DateTimeOffset.MinValue;
    private string? _pendingText;
    
    public void Configure(Func<string, CancellationToken, Task> editAction, CancellationToken ct);
    
    public void Submit(string text, bool immediate = false)
    {
        // If immediate or first edit or enough time passed → flush now
        // Otherwise → schedule flush after remaining interval
    }
    
    public async Task FlushAsync();  // Wait for pending flush
}
```

**Immediate flush triggers:** errors, confirmation required (don't want user waiting).

#### 5. ProgressMessageFormatter

Formats stages into rich Telegram messages:

```csharp
public sealed class ProgressMessageFormatter
{
    private readonly List<StageEntry> _entries = [];
    
    public void HandleStage(string stage, string? detail)
    {
        switch (stage)
        {
            case "planning:start":
                _entries.Add(new StageEntry("planning", $"🧠 Planowanie... ({detail})"));
                break;
            case "llm:plan_ready":
                var parts = detail.Split('|');
                ReplaceEntry("llm_plan", $"✅ Plan: {parts[0]} kroków — \"{parts[1]}\" (confidence: {parts[2]})");
                break;
            case "step:start":
                _entries.Add(new StageEntry($"step_{stepNum}", $"⏳ {stepNum}: {capability}", isRunning: true));
                break;
            case "step:done":
                ReplaceEntry($"step_{stepNum}", $"✅ {stepNum}: {capability} — {detail}");
                break;
            // ... more stages
        }
    }
    
    public string Format()
    {
        // 📝 user text
        // 🎯 plan intent (N steps)
        // ✅/⏳ stage entries
    }
}
```

**Key:** `ReplaceEntry(key, newText)` updates existing entries (e.g., ⏳ → ✅).

#### 6. TelegramProductionAdapter Integration

```csharp
if (verbosity >= VerbosityLevel.Full)
{
    var invocationRecorder = new VerbosityStageRecorder();  // No engine needed
    var throttler = new ProgressThrottler(TimeSpan.FromSeconds(1));
    var formatter = new ProgressMessageFormatter();
    formatter.SetUserText(mapped.Text);
    
    throttler.Configure(async (text, token) =>
    {
        await _messenger.EditTextAsync(chatId, progressMessageId, text, token);
    }, ct);
    
    invocationRecorder.OnStage += msg =>
    {
        formatter.HandleStage(msg.Stage, msg.Detail);
        var text = formatter.Format();
        var isImmediate = msg.Stage.Contains("error");
        throttler.Submit(text, immediate: isImmediate);
    };
    
    // Pass recorder to host
    var response = await _host.HandleUpdateAsync(..., invocationRecorder: invocationRecorder);
    
    // Flush pending edits before final response
    await throttler.FlushAsync();
    throttler.Dispose();
    invocationRecorder.Dispose();
}
```

### Example Output (verbosity:full)

```
📝 pobierz ubuntu

🎯 Pobierz ubuntu (2 kroki)

🧠 Planowanie... (LLM)
📊 Kontekst załadowany
🧠 Planowanie z LLM... (42 capabilities, scope: media)
✅ Plan: 2 kroków — "Pobierz ubuntu" (confidence: 0.90)
✅ Plan zatwierdzony
⏳ 1/2: torrent.search
✅ 1/2: torrent.search — 221 wyników
⏳ 2/2: download.start
✅ 2/2: download.start — wymaga potwierdzenia
💬 Generowanie odpowiedzi...
💬 Odpowiedź gotowa
```

### Verbosity Levels

| Level | Command | What user sees |
|-------|---------|----------------|
| **Off** | `/verbosity off` | No progress messages, only final response |
| **Low** | `/verbosity low` | `"parse: received update"` → final |
| **Medium** | `/verbosity medium` | `"parse: received"` → `"plan: submitting"` → final |
| **Full** | `/verbosity full` | Real-time progress with emoji, timing, details |
| **Debug** | `/verbosity debug` | Full + LLM internals (prompt size, response time, parse failures) |

### Debug Verbosity Level

The Debug level extends Full with detailed LLM debugging information:

**Additional stages emitted:**
- `debug:llm:prompt` — Prompt size, capabilities count, scope
- `debug:llm:response` — Response time (ms), size, status (ok/empty/repair)
- `debug:llm:parse_failed` — When LLM returns invalid JSON
- `debug:llm:repair` — Repair attempt number

**Example Debug output:**
```
🧠 Planowanie... (LLM)
🔍 Prompt: 2453 chars, 42 capabilities, scope: media
📥 Response: 3200ms, 0 chars (empty)
⚠️ Parse failed — LLM returned invalid JSON
🔧 Repair attempt: 1/2
📥 Response: 2800ms, 150 chars (repair:1)
✅ Plan: 2 kroków — "Pobierz ubuntu" (confidence: 0.90)
```

**Implementation:**
```csharp
// OllamaLlmPlanner.cs
public async Task<PlanEnvelope> PlanAsync(LlmPlanningRequest request, ...)
{
    var progress = request.ProgressReporter;
    
    progress?.Report("debug:llm:prompt", $"{prompt.Length}|{request.Capabilities.Count}|{request.Scope}");
    
    var response = await _client.GenerateAsync(prompt, ct);
    progress?.Report("debug:llm:response", $"{sw.ElapsedMilliseconds}|{response?.Length ?? 0}|{(string.IsNullOrEmpty(response) ? "empty" : "ok")}");
    
    if (LlmPlanParser.TryParse(response, request, out var plan))
    {
        return plan;
    }
    
    progress?.Report("debug:llm:parse_failed", null);
    
    // Repair loop
    for (var attempt = 1; attempt <= MaxRetries; attempt++)
    {
        progress?.Report("debug:llm:repair", $"{attempt}/{MaxRetries}");
        // ... retry logic
    }
}
```

### Stage Naming Convention

Format: `category:action`

- `planning:start`, `planning:done`
- `context:refresh`
- `llm:planning`, `llm:plan_ready`, `llm:validated`, `llm:validation_error`, `llm:responding`, `llm:responded`
- `step:start`, `step:done`, `step:skip`, `step:error`
- `parse`, `plan`, `execute`, `respond`, `confirm` (from TelegramBotHost)
- `debug:llm:prompt`, `debug:llm:response`, `debug:llm:parse_failed`, `debug:llm:repair` (Debug level only)

### Detail Format

Pipe-delimited for easy parsing:
- `planning:start`: `"llm"` or `"deterministic"`
- `planning:done`: `"{stepCount}|{intent}"`
- `llm:planning`: `"{capabilityCount}|{scope}"`
- `llm:plan_ready`: `"{stepCount}|{intent}|{confidence}"`
- `step:start`: `"{stepIndex}/{totalSteps}|{capabilityName}"`
- `step:done`: `"{stepIndex}/{totalSteps}|{capabilityName}|{detail}"`
- `step:error`: `"{stepIndex}/{totalSteps}|{capabilityName}|{errorMessage}"`

Step details:
- `"count=221"` → "221 wyników"
- `"jobId=abc123"` → "job: abc123"
- `"confirmation_required"` → "wymaga potwierdzenia"

### Key Design Decisions

1. **Per-invocation recorder** — not per-engine. Each request gets its own recorder with OnStage callback. Avoids mixing stages from different requests.

2. **IProgressReporter on Invocation** — pipeline doesn't know who listens. Just calls `progress?.Report(...)`. Works for both Telegram (real-time edits) and CLI (no-op).

3. **Throttle with immediate flush** — 1 edit/sec prevents flickering, but errors/confirmations flush immediately (user shouldn't wait for error feedback).

4. **Formatter replaces entries** — ⏳ becomes ✅ when done. Uses keyed entries so we can update specific stages.

5. **Rich context per operation** — different operations show different details. Search shows count, download shows jobId, etc.

6. **Final verbose response includes progress** — after pipeline completes, final message includes all stages as a log.

### When to Apply This Pattern

- Long-running operations where users need feedback
- Multi-step pipelines where progress matters
- Any scenario where "Working..." for 10+ seconds is unacceptable
- Systems with event-driven architecture (bus, pub/sub)

### Files Modified/Created

| File | Change |
|------|--------|
| `IProgressReporter.cs` | New interface in Contracts |
| `Invocation.cs` | Added `ProgressReporter` property |
| `InvocationPipeline.cs` | Emits progress events (planning, steps) |
| `EngineHost.cs` | Emits progress events (LLM path, context, steps) |
| `VerbosityStageRecorder.cs` | Added `OnStage` event, implements `IProgressReporter` |
| `ProgressThrottler.cs` | New throttler for Telegram edits |
| `ProgressMessageFormatter.cs` | New formatter with rich context |
| `TelegramBotHost.cs` | Per-invocation recorder, passes reporter to invocations |
| `TelegramProductionAdapter.cs` | Subscribes to OnStage, edits message in real-time |

---

## LLM Audit Logging (Always On)

Independent of verbosity level, all LLM interactions are logged to files for post-mortem analysis.

### LlmAuditLogger

```csharp
// TorrentBot.Llm/LlmAuditLogger.cs
public sealed class LlmAuditLogger
{
    private readonly string _logDirectory;  // /tmp/homelynx-llm-audit/

    public void LogPrompt(string role, string prompt, int capabilitiesCount, string? scope);
    public void LogResponse(string role, string? response, long elapsedMs, string? extra = null);
    public void LogPlan(string role, string? intent, int stepsCount, double confidence);
}
```

### Log Files

Location: `/tmp/homelynx-llm-audit/` (or configurable via constructor)

Format: JSONL (one JSON object per line)

- `YYYY-MM-DD_prompt.jsonl` — All prompts sent to LLM
  ```json
  {"timestamp":"2026-07-07T12:34:56Z","role":"planner","prompt_length":2453,"capabilities_count":42,"scope":"media","prompt_preview":"You are TorrentBot..."}
  ```

- `YYYY-MM-DD_response.jsonl` — All responses from LLM
  ```json
  {"timestamp":"2026-07-07T12:34:59Z","role":"planner","elapsed_ms":3200,"response_length":150,"response_preview":"{\"intent\":\"...\"}","extra":null}
  ```

- `YYYY-MM-DD_plan.jsonl` — Parsed plans
  ```json
  {"timestamp":"2026-07-07T12:34:59Z","role":"planner","intent":"Download Ubuntu","steps_count":2,"confidence":0.9}
  ```

### Integration

```csharp
// EngineBootstrap.CreateLlmPipeline
var auditLogger = new LlmAuditLogger();
return new LlmPipeline(
    new OllamaLlmPlanner(plannerClient, auditLogger),
    new AuditingLlmExecutor(new OllamaLlmExecutor(executorClient), auditSink),
    new OllamaLlmResponder(responderClient),
    auditSink);
```

### When to Use

- Debugging LLM planning failures (empty steps, invalid JSON)
- Analyzing prompt engineering effectiveness
- Tracking LLM response times and patterns
- Post-mortem analysis of production issues

### Key Design Decisions

1. **Always on** — Not gated by verbosity. Critical for debugging.
2. **File-based** — Survives process restarts, easy to grep/analyze.
3. **JSONL format** — One object per line, easy to parse with `jq`.
4. **Preview truncation** — Prompts truncated to 2000 chars, responses to 1000 chars (full content in logs).
5. **Thread-safe** — `lock (_gate)` around file writes.
