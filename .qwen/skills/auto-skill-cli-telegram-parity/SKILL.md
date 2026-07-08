---
name: cli-telegram-parity
description: Pattern for making CLI adapter use identical path as Telegram adapter with shared pipeline, presentation, and context
source: auto-skill
extracted_at: '2026-07-06T19:36:19.739Z'
---

# CLI-Telegram Parity Pattern

## Problem
CLI and Telegram adapters had different code paths:
- **Telegram**: Update → TelegramBotHost → InvocationPipeline → Presentation → Response
- **CLI**: Command → CliApplication → Pipeline.RunAsync → Direct output (bypassed Presentation)

This caused:
- Different rendering/formatting between adapters
- Inconsistent behavior
- Harder to test (CLI tests didn't match Telegram behavior)
- More code to maintain

## Solution: CliBotHost Pattern

Create a `CliBotHost` that mirrors `TelegramBotHost` exactly:

```csharp
public sealed class CliBotHost : IAsyncDisposable
{
    private readonly IInvocationPipeline _pipeline;
    private readonly CliInvocationAdapter _adapter;
    private readonly ArtifactPresentation _presentation;
    private readonly ConversationContextStore _contextStore;
    private readonly Stopwatch _requestTimer = new();

    public async Task<CliBotResponse> HandleMessageAsync(
        string text, string userId, string sessionId, bool isDryRun)
    {
        _requestTimer.Restart();
        var context = GetOrCreateContext(sessionId, userId);
        var requestNumber = context.NextRequestNumber();
        
        // Log request
        Console.Error.WriteLine($"[CliBotHost] Request #{requestNumber}: {text}");
        context.AddMessage("user", text, requestNumber);

        // Parse command (same adapter pattern as Telegram)
        var invocation = _adapter.ToInvocation(text, user, isDryRun);

        // Execute pipeline (identical to Telegram)
        var pipelineResult = await _pipeline.RunAsync(invocation);

        // Render response (same presentation layer)
        var rendered = _presentation.Render(
            pipelineResult.Artifacts,
            new RenderContext(RenderChannel.Cli));

        // Track context
        context.AddMessage("assistant", rendered.Text, requestNumber);

        return new CliBotResponse(
            pipelineResult.Success,
            rendered.Text,
            pipelineResult.Artifacts.RawResult,
            rendered,
            pipelineResult.Plan,
            _requestTimer.Elapsed,
            requestNumber);
    }
}
```

## Key Components

### 1. CliInvocationAdapter
Parses text into Invocation (same as TelegramInvocationAdapter):
```csharp
public Invocation ToInvocation(string text, UserContext user, bool isDryRun)
{
    if (text.StartsWith('/'))
        return ParseSlashCommand(text, user, isDryRun);
    
    return new Invocation
    {
        IsExplicit = false,
        Text = text,
        IsDryRun = isDryRun,
        RequestContext = new RequestContext(...),
        User = user
    };
}
```

### 2. Unified Response Record
```csharp
public sealed record CliBotResponse(
    bool Success,
    string Message,
    ExecutionResult? ExecutionResult,
    RenderedOutput? Rendered,
    ExecutionPlan? Plan,
    TimeSpan Duration,
    int RequestNumber);
```

### 3. Context Tracking
- ConversationContext per session
- Request numbering
- Message history (user/assistant)
- Duration tracking

## Usage

### CLI Commands
```bash
# Slash command (same as Telegram)
./cli run "/downloads"

# Natural language (same as Telegram)
./cli run "show downloads"
./cli run "pokaż pobierania"

# With session context
./cli run --session my-session "show downloads"
./cli run --session my-session "how many are active"
```

### Output
```
[CliBotHost] Request #1 from user cli-user: /downloads
[CliBotHost] Explicit command: /downloads
[CliBotHost] Request #1 completed in 2987.23ms
0 download(s) found
[CliBotHost] Duration: 2987.23ms, Request #1
```

## Benefits

1. **Single code path** - CLI and Telegram use identical pipeline
2. **Consistent rendering** - Same Presentation layer for both
3. **Easier testing** - CLI tests match Telegram behavior
4. **Shared context** - ConversationContext works in both
5. **Unified logging** - Same log format, timing, request numbers
6. **Less maintenance** - One pipeline, one presentation, two adapters only

## Implementation Checklist

- [ ] Create `CliBotHost` mirroring `TelegramBotHost`
- [ ] Create `CliInvocationAdapter` mirroring `TelegramInvocationAdapter`
- [ ] Use `Presentation.Render(RenderChannel.Cli)` instead of direct output
- [ ] Add `ConversationContextStore` for session tracking
- [ ] Track request numbers and duration with `Stopwatch`
- [ ] Add `run` command to CLI that accepts text input
- [ ] Log all requests with timing and context info

## Files Modified

- `src/TorrentBot.Adapters.Cli/CliBotHost.cs` (new)
- `src/TorrentBot.Adapters.Cli/CliInvocationAdapter.cs` (new)
- `src/TorrentBot.Adapters.Cli/CliApplication.cs` (added `run` command)

## Testing

```bash
# Test slash command
./cli run "/downloads"

# Test natural language
./cli run "show downloads"

# Test context persistence
./cli run --session test "show downloads"
./cli run --session test "how many are active"
```

Both should show identical behavior to sending the same messages via Telegram.
