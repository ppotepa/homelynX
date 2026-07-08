---
name: llm-context-snapshots
description: Pattern for implementing context-aware LLM systems with live state snapshots and detailed responses for bot assistants
source: auto-skill
extracted_at: '2026-07-06T16:43:08.182Z'
---

# LLM Context Snapshots & Detailed Responses Pattern

When building LLM-powered bots that need to answer questions about system state (downloads, jobs, media, etc.), use a snapshot-based context system and ensure responses include actual data, not just status messages.

## Architecture

```
User Request → ConversationContextStore
                    ↓
              Refresh Snapshots (generic collectors)
                    ↓
              Enriched LLM Prompt
              - Request number (#1, #2, #3...)
              - Live state snapshots
              - Conversation history (last N messages)
                    ↓
              LLM Planner (has context, can answer directly)
                    ↓
              Capability Execution
                    ↓
              Detailed Response Formatter
              - Extract Data dictionary
              - Format lists with details
              - Include counts, names, status
```

## Key Components

1. **ConversationContext** - Tracks session state:
   - Session ID, User ID
   - Request counter (auto-incrementing)
   - Message history (user + assistant, capped at ~20)
   - State snapshots (key-value per source)

2. **IContextCollector** - Interface for state collectors:
   ```csharp
   interface IContextCollector {
       Task<ContextSnapshot> CollectAsync(CancellationToken ct);
       string SourceName { get; }
   }
   ```

3. **GenericContextCollector** - Use generic collectors over typed ones:
   - Route by `source.Name` not by type
   - Avoids circular dependencies between Engine and Plugin projects
   - Extract scalar fields, limit items to ~10 for context

4. **ConversationContextStore** - Manages contexts per session:
   - `GetOrCreate(sessionId, userId)`
   - `RefreshSnapshotsAsync(context)` - calls all collectors
   - Stores collectors registered at bootstrap

## Implementation Pitfalls

### ❌ Don't: Initialize context store in bootstrap before plugins register
```csharp
// BAD: GetSnapshotSources() returns empty because plugins haven't registered yet
public static EngineHost Create(...) {
    engine.RegisterPlugin(new DownloadsPlugin());
    // ...
    var contextStore = new ConversationContextStore();
    foreach (var source in engine.GetSnapshotSources()) { // EMPTY!
        contextStore.RegisterCollector(...);
    }
}
```

### ✅ Do: Initialize context store in StartAsync after Freeze()
```csharp
// GOOD: Plugins have registered their snapshot sources
public Task StartAsync(CancellationToken ct) {
    foreach (var plugin in _pendingPlugins) {
        PluginLoader.RegisterPlugin(plugin, _registrationContext);
    }
    _capabilities.Freeze();
    _repositories.Freeze();

    // NOW snapshot sources are available
    if (_options.ConversationContextStore is null) {
        var contextStore = new ConversationContextStore();
        foreach (var source in _repositories.GetAllSources()) {
            var collector = ContextCollectorFactory.Create(source);
            if (collector is not null) {
                contextStore.RegisterCollector(collector);
            }
        }
        _options.ConversationContextStore = contextStore;
    }
}
```

### ❌ Don't: Create typed collectors per source
```csharp
// BAD: Creates circular dependency
class DownloadsContextCollector {
    DownloadsSnapshotSource _source; // Engine → Plugins dependency
}
```

### ✅ Do: Use generic collectors with name-based routing
```csharp
// GOOD: No circular dependencies
class GenericContextCollector : IContextCollector {
    ISnapshotSource _source; // Only depends on Contracts

    public static IContextCollector? Create(ISnapshotSource source) {
        return source.Name switch {
            "downloads" => new GenericContextCollector(source, "downloads"),
            "media_files" => new GenericContextCollector(source, "media"),
            _ => null
        };
    }
}
```

### ❌ Don't: Return only Message from CapabilityResult
```csharp
// BAD: Loses all the data
return Task.FromResult(lastResult.Message); // "Found 3 downloads"
```

### ✅ Do: Format detailed responses with Data
```csharp
// GOOD: Includes actual data
private static string FormatDetailedResponse(CapabilityResult result) {
    if (result.Data is Dictionary<string, object?> data) {
        var sb = new StringBuilder();
        sb.AppendLine(result.Message ?? "Result:");
        
        foreach (var (key, value) in data) {
            if (value is IEnumerable enumerable and not string) {
                // Format list items with details
                foreach (var item in enumerable.Take(10)) {
                    if (item is Dictionary<string, object?> dict) {
                        // Extract key fields: name, status, progress, etc.
                    }
                }
            }
        }
        return sb.ToString();
    }
}
```

## Prompt Enrichment

Add these sections to the LLM system prompt:

```
## Conversation context
This is request #N in the current session.

## Current system state (live snapshots)
### downloads
- active_count: 3
- completed_count: 12
- items: 3 items

### media
- total_count: 247
- total_size_bytes: 107374182400

## Recent conversation
[user #1] show me active downloads
[assistant #1] You have 3 active downloads...
```

## Verbosity Mode for Debugging

Implement a verbose mode that shows the full pipeline:

```
🔍 VERBOSE MODE

📝 Request: Ile jest plików na liście pobierania?

🎯 Plan: list downloads
   Steps: 1
   • query.execute
     params: source=downloads

✅ Execution: Success

💬 Response:
Found 3 downloads:
  - name: ubuntu.iso, status: downloading, progress: 45%
  - name: fedora.img, status: completed, progress: 100%
  - name: debian.iso, status: paused, progress: 78%
```

Implementation:
```csharp
private static string BuildVerboseResponse(update, response, rendered) {
    var sb = new StringBuilder();
    sb.AppendLine("🔍 VERBOSE MODE\n");
    sb.AppendLine($"📝 Request: {update.Text}\n");
    
    if (response.Plan is { } plan) {
        sb.AppendLine($"🎯 Plan: {plan.Intent}");
        sb.AppendLine($"   Steps: {plan.Steps.Count}");
        foreach (var step in plan.Steps) {
            sb.AppendLine($"   • {step.CapabilityName}");
            // Show parameters
        }
        sb.AppendLine();
    }
    
    sb.AppendLine($"✅ Execution: {(response.Success ? "Success" : "Failed")}");
    sb.AppendLine("\n💬 Response:");
    sb.AppendLine(rendered?.Text ?? response.Message);
    
    return sb.ToString();
}
```

## Async Interface Refactoring

When making interfaces async (sync → async), follow this order:

1. Update interface: `LlmExecutionResult Execute(...)` → `Task<LlmExecutionResult> Execute(...)`
2. Update all implementations (StubLlmExecutor, OllamaLlmExecutor, AuditingLlmExecutor)
3. Update all callers (LlmPipeline, EngineHost)
4. Update tests (add `await`)

Common mistake: Forgetting to `await` the inner call in wrapper/decorator classes.

## Benefits

1. **Fewer capability calls** - LLM can answer "how many downloads?" from snapshot
2. **Better context** - LLM sees request number, understands conversation flow
3. **Faster responses** - No need to call capabilities for simple state queries
4. **Detailed responses** - Users see actual data, not just "Found N items"
5. **Debuggable** - Verbosity mode shows full pipeline for troubleshooting

## When to Use

- Bot needs to answer questions about system state
- Multiple related queries in a session (conversational context matters)
- Want to reduce LLM → capability round-trips
- Need to track conversation flow (request numbering)
- Users complain about vague responses ("Found 3 items" without details)
- Need to debug LLM pipeline behavior
