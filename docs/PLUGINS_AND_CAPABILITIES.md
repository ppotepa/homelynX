# Plugins, Capabilities & Registration

This document explains the plugin model and how capabilities work — the main extension mechanism of the bot.

## Philosophy

- The Engine is small and stable.
- Almost all domain behavior lives in **plugins**.
- Plugins are **isolated** — they only interact with the rest of the system through a narrow `IEngineContext`.
- Registration is **declarative** (attributes + reflection) so the system can discover what is available for ACL, LLM planning, CLI help, etc.

## What Is a Plugin?

A plugin is a unit of functionality that can contribute:

1. **Capabilities** (actions the user or LLM can invoke)
2. **Repositories / Snapshot Sources** (queryable state)
3. Job handlers
4. Background tasks (optional)
5. (Later) other extension points

Example plugins we expect:

- `system` (health, status, config, capabilities listing, find large files)
- `downloads` (DownloadProcessManager implementing the Process Manager pattern + unified "downloads" repository)
  - Providers via `IDownloader` (TorrentDownloader, UrlDownloader, future ones)
  - `torrent` search logic lives inside the torrent provider (Jackett + qBittorrent)
- `query` (the `query.execute` meta-capability)
- `media` 
- `tts`
- `surveillance` (scoped)

## Capability — The Atomic Unit

A capability is a named, documented, permissioned, risk-classified action.

### Declaration (C#)

```csharp
[Capability(
    Name = "torrent.search",
    Command = "/search",
    Description = "Search for torrents using Jackett",
    Permission = "USER",
    Risk = RiskLevel.Safe,
    LlmUsage = "Use when the user wants to find new content to download",
    IntentHints = ["search", "find", "pobierz", "download"],
    RequiresConfirmation = false,
    RedundantWith = new[] { "media.search" }
)]
public sealed class SearchCapability : ICapabilityHandler<SearchParams>
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        SearchParams parameters,
        CancellationToken cancellationToken)
    {
        // implementation uses context to publish events, enqueue jobs, query repos, etc.
    }
}
```

### Capability Metadata (what the system knows)

- `Name` — stable identifier (`torrent.search`, `system.health`, `query.execute`)
- `Command` — optional slash command mapping
- `Permission` (from attribute) — feeds ACL (USER / ADMIN / PUBLIC + presets)
- Risk level (Safe, ConfirmationRequired, Destructive, Admin) — separate from Permission
- Rich hints for LLM (`LlmUsage`, `IntentHints`, `Preconditions`)
- Whether it is read-only, long-running, etc.

This metadata (including `Permission`) is collected at startup into the `CapabilityRegistry`. The registry works together with `AclService` to produce user-filtered capability lists.

This metadata is collected at startup into the `CapabilityRegistry` and is the single source of truth for:
- ACL checks
- Planner decisions
- CLI help / autocompletion
- Audit logs
- Documentation generation

## The Two Ways to Register

### 1. Attribute-driven (preferred)

The system scans for classes implementing `ICapabilityHandler<TParams>` that carry `[Capability]`.

Also supports `[Repository("name")]` on methods or classes that return `ISnapshotSource`.

### 2. Programmatic (for complex cases)

```csharp
public class MyComplexPlugin : IPlugin
{
    public void Register(IEngineContext ctx)
    {
        ctx.RegisterCapability(...);
        ctx.RegisterSnapshotSource(...);
    }
}
```

Most plugins should use attributes.

## Plugin Lifecycle

1. Discovery (at Engine start)
2. Instantiation (the Engine or DI creates the plugin instance)
3. `Register(IEngineContext)` called
4. Plugin contributes capabilities and sources
5. Later: Engine can ask plugins for background tasks or job handlers

Plugins are **not** long-lived singletons that hold the whole world. They are more like "feature modules".

## What a Plugin Can and Cannot Do

**Can:**
- Implement capability handlers
- Provide snapshot sources
- Enqueue jobs via context
- Publish events
- Read limited config
- Use registered services through the context (carefully)

**Cannot:**
- Directly talk to Telegram
- Reach into other plugins
- Access the full configuration or the raw Engine
- Register global handlers outside the capability system

## First Plugin: `system`

We will start with the `system` plugin. It should provide at minimum:

- `system.health`
- `system.status`
- `system.capabilities` (list available capabilities for the current user)
- `system.query_sources` (what can be queried)
- Possibly `system.find_large_files` or similar

This plugin will also expose a small runtime snapshot source so the LLM and humans can ask "what plugins are loaded?" or "how many jobs are running?".

## Capability vs Job

- Capability = the thing the user/LLM asks for
- Job = the implementation mechanism when the work is long or background

A capability handler can:
- Do the work immediately and return
- Enqueue a Job and wait for completion (or return the job id)
- Fire-and-forget a job

The Executor and bus understand job results.

## Example: How `query.execute` Fits

- Declared as a capability (probably in a small `query` plugin or `system`)
- Its handler receives a `QuerySpec`
- It asks the `RepositoryAggregator` for the right source + executes the spec
- Result is returned as a normal `CapabilityResult`

Because it is a normal capability, the planner can call it, ACL applies, risk level applies, etc.

---

## Summary

Plugins + Capabilities are how we achieve:

- Clean separation
- Discoverability for LLM
- Consistent ACL and safety
- Testability (fake a plugin or a single capability easily)
- Incremental porting from the old system

See:
- [ARCHITECTURE.md](ARCHITECTURE.md)
- `docs/ENGINE.md`
- `docs/QUERY.md`
