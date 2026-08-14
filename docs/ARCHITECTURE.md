# Architecture

## Runtime flow

Homelynx is command-driven. Telegram and CLI adapters convert explicit user commands into an `Invocation`. Command routing resolves the target capability. `InvocationPipeline` calls `EngineHost.SubmitAsync`, which applies registry lookup, ACL, confirmation and execution rules. The capability handler calls the required integration and the result is converted into response artifacts for the originating adapter.

```text
Telegram / CLI
    ↓
explicit command parsing
    ↓
SlashCommandRouting / capability resolution
    ↓
InvocationPipeline
    ↓
EngineHost
    ↓
CapabilityRegistry + ACL + confirmation
    ↓
capability handler
    ↓
Jackett / qBittorrent / filesystem media / TTS / DuckDB
    ↓
response artifacts + presenter
    ↓
Telegram / CLI
```

There is no planner and no free-form language-to-command inference.

## Projects

- `TorrentBot.Adapters.Telegram.Host` — production Telegram entry point.
- `TorrentBot.Adapters.Telegram` — Telegram update mapping, command adapter, callback handling and rendering support.
- `TorrentBot.Adapters.Cli` — explicit CLI entry point.
- `TorrentBot.Bootstrap` — composition root for engine, integrations and pipeline.
- `TorrentBot.Engine` — capability execution, ACL/confirmation integration, session state, jobs and event bus.
- `TorrentBot.Contracts` — public records/interfaces shared between projects.
- `TorrentBot.Plugins.*` — capability implementations grouped by domain.
- `TorrentBot.Query` — validated DuckDB query compilation/execution.
- `TorrentBot.Integrations` — Jackett, qBittorrent, media and TTS clients.
- `TorrentBot.Presentation` — channel-specific response rendering.

## State

Conversation/session state is deliberately small and functional. It exists for workflows such as `/search` followed by `/select`, and for pending confirmations. Ordinary text does not resolve pending actions. Telegram callback buttons do.

## Background work

Download jobs and completion monitoring remain asynchronous because downloads outlive an individual command. Job state and completion events are independent of command interpretation.

## Query subsystem

Snapshot sources expose structured runtime data to the Query plugin. `query.execute` validates a structured query specification and executes it through DuckDB. The query subsystem is not an inference engine.

## Security boundary

Capabilities carry risk/scope metadata. `EngineHost` resolves the current user, applies ACL rules, and requires confirmation where configured before destructive or protected operations are executed.
