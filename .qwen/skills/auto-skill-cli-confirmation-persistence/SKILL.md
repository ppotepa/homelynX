---
name: cli-confirmation-persistence
description: Pattern for making in-memory confirmation stores work across CLI process-per-invocation calls via file-based persistence
source: auto-skill
extracted_at: '2026-07-06T21:05:00.000Z'
---

# CLI Confirmation Persistence Pattern

## Problem

Destructive capabilities (e.g., `download.start`, `download.cancel`) require a two-step confirmation flow:

1. First call → engine issues a token, returns `confirmationRequired: true`
2. Second call with `--confirm=<token>` → engine validates token, executes action

This works in long-running processes (Telegram bot) where the same engine instance handles both calls. But CLI uses **process-per-invocation** — each call creates and destroys an engine. In-memory `ConfirmationStore` loses the token between calls.

**Symptom:** Second call always returns "Confirmation required" with a *new* token, because the original store is gone.

## Solution: IConfirmationStore + FileBasedConfirmationStore

### 1. Extract interface from concrete class

```csharp
// TorrentBot.Engine/Confirmations/IConfirmationStore.cs
public interface IConfirmationStore
{
    string Issue(string capabilityName, string userId, TimeSpan? ttl = null);
    bool TryConsume(string token, string capabilityName, string userId);
}
```

Make the existing `ConfirmationStore` implement it:
```csharp
public sealed class ConfirmationStore : IConfirmationStore { ... }
```

### 2. Create file-based implementation

```csharp
// TorrentBot.Engine/Confirmations/FileBasedConfirmationStore.cs
public sealed class FileBasedConfirmationStore : IConfirmationStore
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public FileBasedConfirmationStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Path.GetTempPath(), "homelynx-confirmations.json");
    }

    public string Issue(string capabilityName, string userId, TimeSpan? ttl = null)
    {
        lock (_lock)
        {
            var pending = LoadPending();
            var token = Guid.NewGuid().ToString("N")[..12];
            pending[token] = new PendingConfirmation(
                capabilityName, userId,
                DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(10)));
            SavePending(pending);
            return token;
        }
    }

    public bool TryConsume(string token, string capabilityName, string userId)
    {
        lock (_lock)
        {
            var pending = LoadPending();
            if (!pending.TryGetValue(token, out var confirmation))
                return false;

            pending.Remove(token);
            SavePending(pending);

            if (confirmation.ExpiresAt < DateTimeOffset.UtcNow)
                return false;

            return string.Equals(confirmation.CapabilityName, capabilityName, ...)
                && string.Equals(confirmation.UserId, userId, ...);
        }
    }
    // LoadPending/SavePending use System.Text.Json to read/write the file
}
```

### 3. Update EngineOptions to use interface

```csharp
// Before
public ConfirmationStore? ConfirmationStore { get; init; }
// After
public IConfirmationStore? ConfirmationStore { get; init; }
```

### 4. Inject FileBasedConfirmationStore in CLI

```csharp
// CliApplication.StartEngineAsync
private static async Task<EngineScope> StartEngineAsync(...)
{
    var confirmationStore = new FileBasedConfirmationStore();
    var engine = EngineBootstrap.Create(
        aclService: acl,
        confirmationStore: confirmationStore);
    // ...
}
```

Telegram bot continues using in-memory `ConfirmationStore` (long-running process, no persistence needed).

### 5. Update all consumers to use interface

- `EngineBootstrap.Create(confirmationStore: IConfirmationStore?)`
- `TelegramBotHost(confirmationStore: IConfirmationStore?)`
- `TelegramProductionAdapter(confirmationStore: IConfirmationStore?)`
- `ConfirmationCallbackHandler(confirmationStore: IConfirmationStore?)`

## Key Design Decisions

1. **File location**: `/tmp/homelynx-confirmations.json` — survives process restarts, auto-cleaned by OS
2. **Thread safety**: `lock (_lock)` around all read-modify-write operations
3. **Graceful degradation**: If file is corrupted or missing, returns empty dictionary (no crash)
4. **TTL preserved**: Same 10-minute default expiration as in-memory store
5. **Single-use tokens**: Token is removed from file on consumption (same as in-memory)

## When to Apply This Pattern

Any time you have:
- An in-memory store (Dictionary, ConcurrentDictionary) that needs to survive across process boundaries
- CLI tools that need state between invocations
- Testing scenarios where process-per-invocation model is required

## Files Modified

| File | Change |
|------|--------|
| `IConfirmationStore.cs` | New interface |
| `FileBasedConfirmationStore.cs` | New file-based implementation |
| `ConfirmationStore.cs` | Added `: IConfirmationStore` |
| `EngineOptions.cs` | `ConfirmationStore` → `IConfirmationStore` |
| `EngineBootstrap.cs` | Parameter type → `IConfirmationStore` |
| `CliApplication.cs` | Injects `FileBasedConfirmationStore` in both `StartEngineAsync` and `RunMessageAsync` |
| `TelegramBotHost.cs` | Parameter type → `IConfirmationStore` |
| `TelegramProductionAdapter.cs` | Parameter type → `IConfirmationStore` |
| `ConfirmationCallbackHandler.cs` | Parameter type → `IConfirmationStore` |
