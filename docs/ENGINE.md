# Engine — The Central Orchestrator

**Owner**: Core team  
**Status**: Planning phase complete (see ARCHITECTURE.md §11)

This document describes the design and responsibilities of the `Engine` — the heart of the Media Server Bot.

## Why a Central Engine?

The previous implementation collapsed almost all behavior into one massive `CommandHandlerService` with many mixins. This made:

- LLM planning unreliable (noisy, inconsistent view of the world)
- Slash commands and natural language on completely different paths
- Plugins mutating global state
- Extremely hard to test and reason about

The new Engine exists to own **orchestration** cleanly.

## Core Responsibilities

| Area                    | What the Engine does                                                                 |
|-------------------------|--------------------------------------------------------------------------------------|
| Lifecycle               | Start / Stop, initialize bus, jobs, registry, aggregator                             |
| Plugin system           | Discover, instantiate and register plugins                                           |
| Capabilities            | Own the registry, metadata, and dispatch to handlers                                 |
| Repositories            | Aggregate `ISnapshotSource` implementations from plugins                             |
| Invocation handling     | Accept `Invocation`, decide deterministic vs planned path                            |
| LLM coordination        | Supply manifests + repo summaries to Planner, run Executor through registry          |
| Bus                     | Publish and subscribe to typed messages (events, commands, job updates)              |
| Jobs                    | Create/track (CreateJob), support different lifetimes via Process Managers           |
| Context surface         | Provide narrow `IEngineContext` to everyone else                                     |
| Observability           | Full stack: structured logs + correlation context + audit + verbosity + metrics + traces |
| ACL integration         | Thin adapter so capabilities are checked consistently                                |

The Engine does **not** contain:
- Telegram-specific code
- Raw external client calls (those live in plugins or dedicated services)
- LLM model calls (delegated to LLM Component)

## Engine Lifecycle (High Level)

```csharp
var engine = new Engine(options);

await engine.StartAsync(ct);

// register plugins (or done via discovery inside Start)
engine.RegisterPlugin(new SystemPlugin());
engine.RegisterPlugin(new TorrentPlugin(...));

var result = await engine.SubmitAsync(invocation, ct);

await engine.StopAsync(ct);
```

During start:
1. Build internal bus
2. Initialize Job tracker (storage + update surface)
3. Create CapabilityRegistry
4. Create RepositoryAggregator
5. Run plugin discovery / registration (Capabilities, Repositories, Process Managers, services)
6. Start any recurring jobs
7. Wire cross-cutting concerns (audit, verbosity)

During shutdown (critical for long-lived processes):
- Cancel long-running Process Managers
- Graceful drain / timeout for active Jobs
- Update final Job states
- Flush events and audit records

## Invocation Flow Inside the Engine

1. Receive `Invocation` (from any adapter)
2. Resolve route:
   - If explicit command or slash → direct lookup in registry
   - If plain text → ask LLM Component for a plan
3. Build `ExecutionContext` (user, permissions, traceId, confirmed, dryRun, etc.)
4. For planned path: Executor validates every step
5. Execute:
   - Short/quick → direct handler
   - Long-running → start Process Manager (which creates LongLived Job using RequestContext) or create Transient Job
6. Publish side-effect events on the bus
7. For processes: the initial call often returns quickly with Job reference. Continuation happens asynchronously via events + Process Manager.
8. Return `ExecutionResult` (artifacts + summary or Job reference)

## EngineContext — The Contract for Everyone Else

See full interface in [ARCHITECTURE.md](ARCHITECTURE.md) (section 5.2) and implementation.

It now carries `IRequestContext` (full entry-point context: TraceId, User, Source, Chat/Message ids etc.) for end-to-end correlation.

See [ARCHITECTURE.md](ARCHITECTURE.md) 5.2 and 5.5 for the complete definition and propagation rules (adapters seed it; it flows to logs, Jobs, events, and Process Managers).

Important rules:
- It is intentionally limited.
- It should be easy to fake for unit tests and dry-run scenarios (fakes should supply realistic RequestContext).
- Publishing to the bus and creating/updating jobs (with correlation automatically attached) are the preferred ways.

## Jobs vs Direct Execution + Process Manager Pattern

Not all async work is the same. We follow the **Process Manager (Orchestration) + Job as Correlation/Projection** pattern.

**Direct execution** (preferred for simple work):
- Fast operations
- Reads or simple mutations
- Most control actions (pause, delete)

**Tracked Job**:
- Created by a Process Manager (for complex/long work) or directly (for Transient).
- Used for progress, visibility, cancellation, LLM awareness.

### JobKind + Process Managers

- **Transient** jobs: simple background work, finite, auto-cleaned.
- **LongLived** jobs: owned by a Process Manager (e.g. DownloadProcessManager). The Job is a projection; rich state lives in repositories.
- **Recurring**: maintenance.
- **Control** actions rarely create their own jobs.

### Downloads as LongLived Processes

A download is managed by `DownloadProcessManager` (Process Manager pattern). It creates a LongLived Job for tracking but the authoritative state is in the `downloads` SnapshotSource + the external downloader.

Delete/pause are commands to the Process Manager, not independent jobs.

See full pattern description in [ARCHITECTURE.md](ARCHITECTURE.md) section 4.6.

The Engine provides the job tracking surface. Domain plugins (via their Process Managers) own the lifecycle decisions.

## Relationship with LLM Component

The Engine is the **orchestrator**, the LLM Component is a **service** it calls.

- Engine builds the inputs for planning (manifest + repo summary)
- Engine owns the `CapabilityExecutionPort` that the Executor uses (so validation + execution always go through the registry)
- Engine decides when to use deterministic renderer vs asking the Responder

## Current Implementation Priorities (Engine)

1. Basic `Engine` + `IEngineContext` skeleton
2. Simple in-memory `IInternalBus`
3. Minimal `Job` model + in-memory queue/runner
4. `CapabilityRegistry` + attribute scanning + basic execution
5. `RepositoryAggregator` + `ISnapshotSource` protocol
6. `query.execute` as a built-in or `System` plugin capability
7. Dry-run friendly `EngineContext` implementation for CLI

## Testing Strategy for the Engine

- Unit test the registry, bus, job queue in isolation
- Integration test: register a fake plugin → submit invocation → verify result + events
- Dry-run adapter that substitutes a recording/fake context
- Property-based or example-based tests for the query path

---

See also:
- [ARCHITECTURE.md](ARCHITECTURE.md) — overall system
- `docs/QUERY.md` — how querying fits into the Engine
- `docs/PLUGINS_AND_CAPABILITIES.md` (to be written)
