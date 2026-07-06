# Homelynx — New Application Architecture (Implementation Blueprint)

**Version**: 1.0 — Full Rewrite Specification  
**Date**: 2026-07-05  
**Status**: Authoritative design for the next iteration of the application logic.  
**Scope**: Rewrite of the *application logic only*. Docker, compose files, setup scripts, installer, media layout, external services (qBittorrent, Jackett, Jellyfin, Ollama, TTS, surveillance, portal, coord-input), `.env`, `allowed-users.cfg`, `acl/presets.cfg`, logging paths, and host bootstrap remain **frozen** as-is.

This document is the single source of truth for the architectural rewrite. It was produced by exhaustive analysis of the existing repository (`src/`, `plugins/`, `services/`, `cli/`, tests, configs, ARCHITECTURE_V2.md, etc.).

---

## 1. Executive Summary + Current State (≈300 words)

Homelynx is a private-homelab media automation system. Primary user surface is a Telegram bot (multi-scope: media, surveillance, coord) with a very capable diagnostic CLI (`homelynxctl`). Core value: torrent search (Jackett) → selectable download (qBittorrent) → media library (Jellyfin), plus optional local LLM (Ollama), TTS, surveillance recorder, and coordinate tracking. All wired together in Docker Compose with mature bootstrap (`setup_linux.sh`).

**The fatal flaw is in the Python application layer** (the part we are rewriting).

The entire runtime behavior is collapsed into one god object: `CommandHandlerService` (src/core/command_handler.py + 15–19 mixin classes in `src/bot/telegram/handlers/*` and `src/bot/telegram/*`). Total handler sprawl ≈ 3600+ LOC in a single inheritance tree. It owns:
- Every slash command handler
- Sessions (search, torrent list)
- Pending confirmations + LLM actions
- Capability registry + dispatcher
- LLM pipeline (planner/executor/responder)
- Query agent + DSL execution
- Verbosity reporter + live message editing
- ACL adapter
- Plugin mutation surface (`register_command`, direct access to torrent_service, bot, config, etc.)
- Download monitoring, callbacks, Telegram message assembly

Consequences:
- LLM planner receives a noisy, inconsistent manifest and cannot reliably produce plans because business logic, side effects, and presentation are entangled.
- Slash commands (deterministic, working) and natural language (planner → steps) follow completely different code paths.
- Hot plugins (`plugins/hot/*.py` via `setup(command_handler)`) are given the entire god object — they mutate global state.
- There is *nascent* clean architecture that is **not wired in**: `src/core/events.py` (full EventBus), partial `src/core/commands.py` (CommandBus skeleton), `src/repository/` (SnapshotManager + DuckDB QueryEngine + SnapshotSource protocol), declarative `@capability` metadata with rich hints (`llm_usage`, `redundant_with`, `intent_hints`), and per-domain capability builders in `src/domains/*/capabilities.py`.
- The repository idea (plugins expose data via IRepository/SnapshotSource) exists in `src/repository` and is used for `query.execute`, but is not the central collaboration mechanism.
- No internal bus, no job queue, no engine that owns orchestration.
- Reflection/decorators are used only for capabilities; everything else is manual wiring and mixin magic.
- CLI is excellent for iteration (`agent plan|run`, `llm raw`, dry-run adapters) but talks to the same messy core.

**What works today**:
- Slash commands are reliable and gated by ACL.
- Natural language + planner + query DSL works for simple cases.
- Audit, verbosity, confirmations, and dry-run are production-grade.
- Infrastructure and external services are solid.

**What we will build**:
A clean, plugin-oriented engine with:
- Explicit input adapters (CLI first-class for tests + Telegram).
- Central `Engine` that owns an internal message/job bus + queue.
- Declarative plugins that register **Capabilities** (commands) **and optionally Repositories**.
- Central `RepositoryAggregator` (in-memory by default, pluggable).
- Two-model LLM component (Planner + Executor) that returns JSON step plans and composes replies.
- Everything discovered via reflection + established conventions (no god objects).
- Start migration by extracting a single `system.health` / `system.status` plugin.

The rewrite is **incremental and parallelizable**. Old and new can coexist during migration. Docker, setup, and external topology are out of scope.

---

## 2. Goals, Non-Goals, and Constraints

### Goals
- One orchestrating **Engine** with clear lifecycle.
- Internal asynchronous bus + typed job/message queue.
- Plugins are first-class, isolated, and discoverable by convention/reflection.
- Every plugin may (but need not) expose one or more `IRepository` implementations; the engine aggregates them.
- Two distinct invocation styles with unified execution:
  1. Deterministic slash/CLI commands → direct handler.
  2. Plain text → LLM Planner produces JSON plan → Executor validates + runs steps → optional Responder composes message.
- Two-model LLM strategy (planner model for intent→steps, executor/responder model for validation/execution/reply).
- Full use of decorators + reflection for registration (`@capability`, `@command`, `@repository`, `@job_handler`, etc.).
- Clean separation: Engine / LLM Component / Repositories / Capabilities / Adapters / ACL / Query.
- Start with a tiny `system` plugin that provides `health` and `status`.
- Preserve (and improve) the excellent CLI as the primary development and test surface.
- Keep 100% of current deterministic behavior and ACL guarantees.
- Excellent testability and dry-run support.

### Non-Goals (for this phase)
- Changing Docker Compose, images, or service topology.
- Rewriting the media organizer script, surveillance recorder, TTS service, Android app, or portal.
- Replacing qBittorrent/Jackett/Jellyfin/Ollama.
- Big-bang cutover — migration must be incremental.
- Adding new external protocols (FTP etc.) until the engine exists.
- Public internet exposure or new auth systems.

### Constraints
- Python 3.12+, asyncio.
- Existing env var contract and config loading stay.
- ACL (`src/acl/`) is mature and must be reused/adapted with zero behavior change.
- Existing query DSL + DuckDB engine is valuable — evolve, do not discard.
- Audit trail to portal SQLite must remain complete.

---

## 3. Guiding Principles

1. **Engine owns orchestration**. Adapters, plugins, LLM, and repositories talk *to* the engine or through its bus, never directly to each other.
2. **Plugins have narrow surface**. A plugin declares commands (capabilities) and optional repositories. It does not see the Telegram bot, the full config, or other plugins' internals.
3. **Declarative over imperative registration**. Use decorators + reflection + naming conventions. A single `PluginManifest` or discovery pass at startup.
4. **Slash = deterministic, plain = planned**. But both ultimately resolve to the same executable capability steps.
5. **Repository per plugin (optional)**. If a plugin has state worth querying (downloads, media, jobs, events), it registers an `IRepository` (or `SnapshotSource`). The aggregator makes it available uniformly via `query.execute` and internally.
6. **Two-model LLM**. Planner (small/fast, produces structured plan) + Executor (validates against live registry, executes, returns artifacts). Responder can be the same as executor model or separate.
7. **Internal bus + job queue is the spine**. Events for side effects, Commands for intent, Jobs for background/long-running work with status, retries, cancellation.
8. **Everything is observable**. Every step, plan, job transition, and repository refresh is auditable. Verbosity is a cross-cutting concern on the bus.
9. **Reflection is the convention**. `@capability`, `@repository(name=...)`, `@handles(JobType)`, class attributes, module-level `setup(engine_context)` as last resort for complex plugins.
10. **CLI is a first-class citizen**. The CLI must be able to invoke the engine directly (bypassing Telegram) for tests, dry-runs, and autonomous DSL usage.

---

## 4. High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        INPUT ADAPTERS                               │
│  CLI (homelynxctl, agent, raw, capability call)   Telegram Bot     │
│         (primary for dev/test)                     (production)    │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                           ENGINE (Orchestrator)                     │
│  • EngineContext / Composition Root                                 │
│  • CapabilityRegistry (reflection-loaded)                           │
│  • RepositoryAggregator (collects IRepository from plugins)         │
│  • InternalBus (Commands, Events, Jobs, Control)                    │
│  • JobQueue + JobRunner (background, durable in-mem first)          │
│  • ACLService (thin adapter)                                        │
│  • AuditSink                                                        │
└───────┬───────────────┬───────────────────────┬─────────────────────┘
        │               │                       │
        ▼               ▼                       ▼
┌───────────────┐  ┌──────────────┐   ┌───────────────────────────────┐
│  LLM Component│  │   Plugins    │   │      Repositories             │
│  • Planner    │  │  (declarative)│   │  (per-plugin optional)        │
│  • Executor   │  │  • torrent   │   │  • QBitTorrentSnapshotSource  │
│  • Responder  │  │  • tts       │   │  • MediaFiles...              │
│  • JSON steps │  │  • system    │   │  • PluginFooRepo (new)        │
└───────────────┘  │  • ...       │   └───────────────────────────────┘
                   └──────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    EXTERNAL / INFRA (unchanged)                     │
│  qBittorrent • Jackett • Jellyfin • Ollama • TTS svc • Surveillance │
│  Portal (audit) • Filesystem snapshots • DuckDB query engine        │
└─────────────────────────────────────────────────────────────────────┘
```

**Request Flow (high level)**

1. Adapter receives input (CLI text or Telegram message).
2. Adapter normalizes to `Invocation` (text, user, chat, source, permission, is_slash, raw_command?).
3. Engine receives `Invocation`.
4. If slash or explicit command → resolve directly to capability + execute.
5. If plain text → LLM Planner (with current manifest + repo summaries) → `PlanEnvelope` (JSON steps).
6. Executor validates every step against registry + ACL + preconditions.
7. Executor runs steps (may enqueue Jobs).
8. Side effects published on bus (events).
9. Responder (or deterministic renderer) produces final reply.
10. Adapter delivers reply (edit in place for Telegram verbosity, print JSON for CLI).

---

## 5. Core Modules & Bounded Contexts (Detailed)

### 5.1 `core` / `foundation`
- `InternalBus` (evolve the existing EventBus + add CommandBus + JobBus on top of one implementation or separate channels).
- Typed messages: `DomainEvent`, `Command`, `JobMessage`, `ControlMessage`.
- `Job` model: id, type, payload, status (queued/running/done/failed/cancelled), progress, result, error, created, updated, owner (chat/user).
- `JobQueue` + `JobRunner` (worker pool, at-least-once in-mem first, later pluggable backend).
- Engine lifecycle: `start()`, `stop()`, `register_plugin(...)`, `submit_invocation(...)`.
- `EngineContext` — the narrow surface given to plugins and adapters (never the whole engine).

### 5.2 `capabilities`
- Keep and harden the existing excellent `CapabilityMeta`, `CapabilityParam`, `CapabilityRisk`, `@capability` decorator.
- `CapabilityRegistry` becomes owned by Engine.
- Add `execution_contract` or `handler` that is a pure async function `(ctx: ExecutionContext, params) -> CapabilityResult`.
- Remove any Telegram-specific knowledge from capability metadata/handlers.
- `CapabilityContext` / `ExecutionContext` is narrow (user, permission, services via engine context, confirmed flag, trace_id).

### 5.3 `llm` (LLM Component — separate bounded context)
- `Planner`: takes text + facts + capability manifest + repository summary → structured `PlanEnvelope` (intent, confidence, reply_mode, steps[], notes).
- `Executor`: takes plan → validates every step (name exists, ACL, risk, requires_confirmation, preconditions) → executes in order or parallel where safe → produces `ExecutionTrace` + artifacts.
- `Responder`: optional second pass or same model to turn execution artifacts + original plan into human reply (or use deterministic renderer for most cases).
- Contracts (`PlanStep`, `PlanEnvelope`, etc.) become first-class and versioned.
- The component is **stateless**; it receives an `LLMAdapter` or `CapabilityExecutionPort` from the engine (dry-run in CLI, real in prod).
- Planner and Executor can be configured with different models via env (`LLM_PLANNER_MODEL`, `LLM_EXECUTOR_MODEL` or fallback).

**Important**: The LLM component never executes shell, never sees raw Telegram objects, never bypasses the capability registry.

### 5.4 `plugins` + Plugin Contract
New contract (old hot plugins will be ported one by one):

```python
# plugins/system/plugin.py  (or package)

from engine.plugin_api import Plugin, capability, repository

class SystemPlugin(Plugin):
    name = "system"
    version = "1.0"

    @capability(
        name="system.health",
        command="/health",
        description="...",
        permission="PUBLIC",
        risk="safe",
        ...
    )
    async def health(self, ctx, params):
        return {"status": "ok", "engine_uptime": ...}

    @repository(name="system.status")
    def status_snapshot_source(self):
        return SystemStatusSnapshotSource(...)   # implements SnapshotSource
```

- Discovery: engine scans `PLUGIN_DIR` (or explicit list) + uses `importlib` + looks for `Plugin` subclasses or module-level `register(engine_context)`.
- At registration time plugin may contribute:
  - Capabilities (via decorator collected by metaclass or explicit list).
  - Zero or more `SnapshotSource` / `IRepository` implementations.
  - Job handlers (via `@handles(JobType.FOO)` or registration).
  - Background tasks (started/stopped by engine).
- Plugin gets only `EngineContext` (bus publish/subscribe, enqueue_job, query_repo, get_service("name"), logger, config subset).

Old `setup(command_handler)` contract is deprecated and will be removed after migration.

### 5.5 `repositories`
- `IRepository` / `SnapshotSource` protocol (already exists and is good — evolve `src/repository/protocols.py`).
- `RepositoryAggregator` (central in Engine):
  - `register(source: SnapshotSource)`
  - `sources() -> list[str]`
  - `query(source, spec) -> QueryResult`
  - `snapshot(source) -> Snapshot`
  - Background refresh loop (already in SnapshotManager — promote it).
- Every plugin that wants to expose queryable data implements the protocol and registers during its `register` phase.
- Default sources remain: qBittorrent downloads, media files.
- In-memory is default. Later: persistent (SQLite/DuckDB file) or remote.

### 5.6 `domains` (or `features`)
Bounded contexts that own both capabilities + their repositories (when applicable):
- `system` (health, status, disk, find_large_files, help, capabilities, config) — **first to implement**
- `torrent`
- `media_download`
- `tts`
- `query` (the `query.execute` meta-capability that routes to aggregator)
- `surveillance`, `bot_control`, etc. (later)

Each domain can be a plugin or a built-in module. Start by making `system` a real plugin.

### 5.7 `adapters`
- `cli` — becomes a thin adapter that:
  - Builds a real or dry-run `EngineContext`.
  - Submits invocations.
  - Renders results (JSON, text, tables).
  - Supports `--dry-run`, audit capture, direct capability calls.
- `telegram` — adapter that:
  - Receives updates.
  - Normalizes to `Invocation`.
  - Calls engine.
  - Handles live verbosity editing, buttons, confirmations, file uploads using engine-provided artifacts.
  - Never contains business logic.

### 5.8 `acl`, `query`, `audit`
- Reuse `src/acl/` almost unchanged (it is already excellent and isolated).
- `query/` (DSL compiler + DuckDB engine + sources) moves under repositories or stays as a supporting library. The `query.execute` capability becomes a first-class capability that the executor can call.
- Audit sink is injected into engine; every step, plan, job, repo refresh produces audit records.

### 5.9 `bus` and `jobs` (the spine)
Messages on the bus are the only way domains/plugins communicate asynchronously:
- `TorrentDownloadStarted`, `JobProgressUpdated`, `RepositoryRefreshed`, `CapabilityExecuted`, etc.
- Jobs are the unit for long-running or background work (download monitor, large file scan, surveillance analysis, LLM summary jobs, etc.).
- Job state is queryable (so a plugin can register a "jobs" repository).

---

## 6. Detailed Request Lifecycles

### 6.1 Deterministic Slash / Explicit Command Path

```
Adapter
  → normalize("/search ubuntu", user, chat, permission, source="telegram|cli")
  → Engine.submit(Invocation(text="/search ubuntu", is_explicit=True, command="search", args=["ubuntu"]))
  → Engine resolves capability "torrent.search" (or by command)
  → ACL check
  → Build ExecutionContext
  → Call capability handler directly (or via CommandBus)
  → Handler may enqueue Job or call external client
  → Collect CapabilityResult + side effects
  → Publish events on bus
  → Return result envelope to adapter
  → Adapter renders (Telegram edit or CLI print)
```

### 6.2 Natural Language (Plain Text) Path

```
Adapter
  → Invocation(text="pobierz ubuntu 24.04", is_explicit=False, ...)
  → Engine
    → facts = deterministic_parser(text)   # cheap
    → manifest = registry.manifest_for_permission(...)
    → repo_summary = aggregator.compact_manifest()   # important for planner
    → plan, meta = Planner.plan(..., manifest, repo_summary)
    → audit plan
    → Executor.execute_plan(plan):
        for step in plan.steps:
            validate(step.capability, ACL, risk, confirmation, preconditions)
            if needs_confirmation and not confirmed: create PendingAction
            result = execute(step)   # may be sync handler or enqueue Job + wait
            record step result
    → artifacts = execution_results + original facts
    → reply = Responder.compose(artifacts, plan) OR deterministic_renderer
    → audit everything
  → return to adapter
```

Key contracts:
- `PlanEnvelope` (intent, reply_mode, confidence, steps: List[PlanStep], notes)
- `PlanStep` (capability, params, why, parallel_safe, confirmation_token?)
- Execution must be idempotent where possible or explicitly guarded.

### 6.3 Confirmation & Pending Actions
- Risk levels drive behavior: SAFE, CONFIRMATION_REQUIRED, DESTRUCTIVE, ADMIN.
- Executor/Engine can create a pending action token.
- Adapters present buttons; callback resolves the token and re-enters execution with `confirmed=True`.

### 6.4 Verbosity / Live Progress
- Implemented as bus subscribers that the Telegram adapter listens to.
- One message per top-level invocation; edits are rate-limited.
- Stages: parse, plan, validate, execute:<cap>, respond, done.
- CLI can also subscribe for progress (or just get final JSON).

---

## 7. Job & Message Model (First-Class)

```python
@dataclass(frozen=True)
class Job:
    id: str
    type: str
    payload: dict
    status: Literal["queued", "running", "succeeded", "failed", "cancelled"]
    progress: float
    result: Optional[dict]
    error: Optional[str]
    owner_chat: Optional[int]
    owner_user: Optional[int]
    created_at: datetime
    updated_at: datetime
    ttl_seconds: Optional[int]
```

- Enqueue returns job id immediately.
- Subscribers on bus can react (`JobCompleted` → notify Telegram, refresh repo, etc.).
- Long-running download monitor becomes a recurring job or dedicated job type.
- Repositories can be refreshed on job events (e.g., after download finishes).

---

## 8. Plugin & Registration Conventions (Reflection)

1. Decorator-driven (preferred):
   - `@capability(...)` on methods or free functions (bound at registration).
   - `@repository(name="foo")` on methods/factories that return `SnapshotSource`.
   - `@handles("job_type")` or `@job_handler(JobType.DOWNLOAD)`.

2. Class-based (for complex plugins):
   ```python
   class MyPlugin(Plugin):
       def register(self, ctx: EngineContext):
           ctx.register_capability(...)
           ctx.register_repository(...)
   ```

3. Module convention (fallback):
   - `def register(ctx: EngineContext): ...`
   - Or top-level `PLUGIN = SystemPlugin()`

4. Discovery order:
   - Explicit list in config.
   - `PLUGIN_DIR/*.py` + `PLUGIN_DIR/*/__init__.py`.
   - Ignore `_` prefixed.
   - Load order deterministic (sorted by name).

5. EngineContext given to plugins (narrow):
   - `publish(event)`
   - `subscribe(event_type, handler)`
   - `enqueue_job(type, payload, ...) -> job_id`
   - `query(source, spec)`
   - `get_capability(name)`
   - `get_config(key, default)`
   - `get_logger()`
   - `register_background_task(...)`

---

## 9. First Deliverable: `system` Health/Status Plugin

MVP goal: replace the current `system.status`, `system.health` (if any), `/ping`, `/diag`, `/llm` (status part) with a clean plugin.

The plugin will:
- Register 4–6 capabilities using the new decorator.
- Optionally expose a small `system.runtime` snapshot source (uptime, loaded plugins, bus stats, job counts).
- Be loadable both in the new engine and (temporarily) via adapter to old command handler during migration.

This proves the entire registration, execution, repository, and LLM path end-to-end with minimal blast radius.

---

## 10. Proposed Final Source Layout (after full migration)

```
src/
  adapters/
    cli/
    telegram/
  capabilities/          # core metadata + registry + decorators (pure)
  core/
    bus.py
    engine.py
    jobs.py
    context.py           # EngineContext
    lifecycle.py
  domains/               # or features/ — can be thin or full plugins
    system/
      __init__.py
      plugin.py          # the first one
      capabilities.py    # or inline decorators
      repositories.py
    torrent/...
  llm/
    component.py         # Planner, Executor, Responder orchestration
    contracts.py
    prompts/
  plugins/
    loader.py            # new discovery
    api.py               # Plugin base, decorators, protocols
    hot/                 # legacy compatibility shim (temporary)
  query/                 # evolve existing (or move under repositories)
  repositories/
    aggregator.py
    protocols.py
    snapshot.py
    sources/             # built-in sources (qbittorrent, media, jobs, system)
  acl/                   # almost unchanged
  audit/
  config/
  main.py                # new composition root
  ...
```

Legacy `src/bot/telegram/handlers/*`, the giant `command_handler.py`, and old mixins will be deleted after migration of each domain.

---

## 11. Migration Strategy (Concrete Steps)

**Phase 0 — Foundations (parallel to old code)**
1. Promote `src/core/events.py` + finish `src/core/commands.py` into a unified `core/bus.py`.
2. Extract `Job` model + `JobQueue` + simple in-memory runner.
3. Create `Engine` + `EngineContext` skeletons (no-op implementations).
4. Harden `capabilities/` as a standalone library.
5. Create `repositories/aggregator.py` that wraps existing SnapshotManager.

**Phase 1 — First Plugin (system)**
1. Implement `domains/system/plugin.py` (or `plugins/system/`) with health + status + a couple more using new decorators.
2. Wire a minimal Engine that can register this plugin and execute capabilities directly.
3. Build a dry-run CLI path that uses the new engine (parallel to `homelynxctl agent`).
4. Prove LLM planner can plan against the clean manifest produced by the new registry.

**Phase 2 — Adapters**
1. Build thin Telegram adapter that submits to Engine.
2. Port live verbosity, confirmations, buttons as bus-driven concerns.
3. Make CLI able to drive real engine for `agent run`.

**Phase 3 — Port Domains One by One**
- torrent (search is the hardest — keep sessions as job state or engine-managed context)
- tts
- media_download
- query (as meta capability)
- surveillance (scoped)
- etc.

**Phase 4 — Cutover & Deletion**
- Old CommandHandlerService becomes a compatibility shim.
- Delete mixins.
- Remove hot-plugin mutation of god object.
- Update docs, e2e, tests.

During all phases the old bot can keep running (different scope or feature flag `ENGINE_V2=true`).

---

## 12. Key Interfaces (Sketches — implement these first)

```python
# core/context.py
class EngineContext:
    def publish(self, event: DomainEvent): ...
    def subscribe(self, event_type, handler, priority=0): ...
    def enqueue_job(self, type: str, payload: dict, *, owner_chat=None, ...) -> str: ...
    async def query(self, source: str, spec: QuerySpec) -> QueryResult: ...
    def get_capability(self, name: str) -> Optional[CapabilityMeta]: ...
    ...

# plugins/api.py
class Plugin(ABC):
    name: str
    version: str
    def register(self, ctx: EngineContext) -> None: ...

def capability(**meta) -> Callable: ...
def repository(name: str) -> Callable: ...

# llm/component.py
class LLMComponent:
    async def plan(self, text: str, context: InvocationContext, manifest: list[dict], repo_summary: list[dict]) -> PlanEnvelope: ...
    async def execute(self, plan: PlanEnvelope, exec_port: CapabilityExecutionPort, ctx: InvocationContext) -> ExecutionResult: ...
```

---

## 13. What Stays Exactly As-Is (Infrastructure Contract)

- `Dockerfile`, `docker-compose*.yaml`, all service definitions and volumes.
- `setup_linux.sh` + all scripts in `scripts/setup/`.
- `.env.example`, environment variable names and semantics.
- `allowed-users.cfg` + `acl/presets.cfg` format and `src/acl/` implementation.
- Media layout (`/media`, `/downloads/{completed,incomplete,...}`).
- qBittorrent, Jackett, Jellyfin, Ollama, TTS, surveillance, coord-input, portal service behavior and ports.
- Existing e2e test harness structure and reports (they will drive the new engine later).
- `homelynxctl` command names and many flags (we evolve the implementation under them).

---

## 14. Implementation Instructions for Agents / Humans

1. **Never** edit the giant `CommandHandlerService` or its mixins for new logic.
2. All new code goes under the new module structure (create directories as needed).
3. Start every significant piece by writing or updating the corresponding section in this document if behavior changes.
4. Use `rtk` for all shell commands during development.
5. Prefer batch tool calls.
6. For every capability, write both the declarative metadata + a pure handler.
7. Every repository source must implement the protocol and be refreshable.
8. LLM planner must receive a compact manifest + repository summary — keep token usage low.
9. All state-changing or long-running work must be expressible as a Job.
10. When porting an existing handler, first extract the pure business logic, then wrap it as a capability.
11. CLI dry-run adapter must be able to drive the new path from day one.
12. Keep ACL behavior byte-for-byte identical during migration.

---

## 15. Open Questions / Future Extensions (not for v1)

- Persistent job queue backend (Redis / SQLite / Postgres).
- Multi-tenant / multiple engine instances.
- Web adapter (FastAPI) as another input.
- Plugin marketplace / signed plugins.
- Download sources beyond torrent (HTTP, FTP, Usenet) — will be new downloader plugins that register capabilities + repositories.
- Cross-plugin transactions / sagas on the bus.

---

## 16. Appendix — Artifacts to Study / Port

**Must read before coding**:
- `src/capabilities/` (all)
- `src/acl/` (all) — do not break
- `src/repository/` (all)
- `src/query/` (contracts + engine + dsl)
- `src/llm/` (contracts, planner, executor, pipeline, deterministic)
- `src/core/events.py` + `src/core/commands.py`
- `src/domains/providers.py` + all `domains/*/capabilities.py`
- `src/cli/dry_run_adapter.py` and `src/cli/commands/agent.py`
- `plugins/hot/hello.py`
- `plugins/torrent/` (to understand what will become the torrent plugin)
- `src/main.py` and `Application` class (composition root)
- `docker-compose.yaml` + relevant env vars in `src/config/settings.py`
- Existing tests under `tests/capabilities/`, `tests/llm/`, `tests/repository/`, `tests/query/`

**Delete after migration** (not before tests pass against new engine):
- Most of `src/bot/telegram/handlers/`
- The mixin explosion in `src/bot/telegram/`
- The body of the old `CommandHandlerService`

---

## 17. Port & Reuse Map (Reference → New Architecture)

The reference repo already contains high-quality isolated pieces. We **port and adapt** rather than re-invent:

| Component in Ref                          | Target in New Layout                     | Notes / Improvements |
|-------------------------------------------|------------------------------------------|------------------------|
| `src/capabilities/decorators.py`, `base.py`, `registry.py` | `src/capabilities/` (or `src/plugins/api.py`) | Excellent. Keep decorator. Enhance registry to live inside Engine. Support binding real handler fns + metadata in one pass. |
| `src/repository/protocols.py` + `manager.py` + `snapshot.py` + sources | `src/repositories/` (protocols + aggregator.py) | `SnapshotManager` is ~90% of the future `RepositoryAggregator`. Rename/adapt `SnapshotManager` → core of aggregator. Plugins register `SnapshotSource` impls. |
| `src/llm/contracts.py` (PlanEnvelope, PlanStep, ...) + planner/executor/responder | `src/llm/` (contracts preserved, component.py new) | Reuse contracts verbatim. New `LLMComponent` orchestrates planner (intent→plan) + executor (validate+run via port) + responder. |
| `src/core/events.py` + `src/core/commands.py` | `src/core/bus.py` (or internal_bus) | Start here. Unify or compose: EventBus + CommandBus + new JobBus/JobQueue on top. Keep global accessors for transition. |
| `src/domains/*/capabilities.py` + `providers.py` | Seeds for `domains/*/plugin.py` or `plugins/*/ ` | Metadata lists are gold. Turn each into a `Plugin` subclass that uses `@capability` on real handler methods (or registers). Co-locate optional repo sources. |
| `src/acl/`                                  | `src/acl/` (copy/adapt as-is)            | Mature, zero behavior change. Thin adapter in EngineContext. |
| `src/query/` (DSL + DuckDB + engine)        | Keep as supporting lib under `src/query/` or `repositories/query.py` | `query.execute` becomes a capability provided by a `query` plugin or built-in. |
| `src/cli/dry_run_adapter.py` + agent cmds   | `src/adapters/cli/` + dry run engine ctx | Use as model for new CLI adapter that builds Engine + EngineContext (real or dry). |
| `plugins/hot/hello.py` (setup pattern)      | Temporary shim only                       | Old mutation pattern. New plugins use declarative decorators + register(ctx). |
| `src/main.py` + Application                 | `src/main.py` (new slim composition root) | New one: construct Engine, discover/load plugins, wire adapters (cli/telegram), start. Much smaller. |

**Strategy for reuse**:
- For pure modules (acl, query, capabilities base, llm contracts, repo manager) → copy into `torrent-bot2/src/` or import via PYTHONPATH from reference during early dev (prefer copy for clean ownership).
- For behavior, extract pure functions first, then wrap as capability handlers inside plugin classes.
- Do not port the god objects, the handler mixins, or plugin `setup(command_handler)`.

Update this table as ports happen.

---

## 18. Implementation Notes & Next Steps (updated)

This document is deliberately long and detailed because the request was for "jeden wielki jakby implementacyjny" spec with "dokładną architekturę", "cykl życia", "każdy moduł ma swoje własne życie", and "z najdrobniejszymi szczegółami".

Use it to drive implementation. When in doubt, come back to the principles and the two invocation paths (slash vs planned) plus the plugin → capability + optional repository model.

**Next concrete actions (prioritized)**:
1. Scaffold directories + copy/adapt pure foundations (capabilities, repo protocols/manager as aggregator seed, buses, llm contracts) into `torrent-bot2/src/`.
2. Implement `core/engine.py` + `core/context.py` (Engine + narrow EngineContext) + minimal `InternalBus` / `Job` skeleton.
3. Harden `plugins/api.py`: `Plugin` base + `@capability` (re-export or adapt) + `@repository` + `EngineContext` surface.
4. Build first plugin: `domains/system/plugin.py` (or `plugins/system/plugin.py`) providing `system.health`, `system.status` (and 1-2 more) as real executable handlers + optional status snapshot source.
5. Wire a minimal Engine that can `register_plugin` and `execute_capability` (or submit Invocation) directly.
6. Create thin `adapters/cli/` that can drive the engine (support dry-run, direct calls, plan mode later).
7. Prove end-to-end: CLI invokes new path → capability executes → result.
8. Extend to LLM planner path using clean manifest from new registry + repo summary.
9. Port adapters (Telegram) and more plugins incrementally.

**During implementation**:
- All new logic lives only in new structure.
- Keep ACL byte-identical.
- Make heavy use of the existing rich metadata for planner (llm_usage, intent_hints, preconditions etc.).
- Every long-running or side-effecting op should be expressible as Job enqueue.
- Test with the excellent CLI surface from day one.

---
reference repo to be used for some stuff (config, docker, reusable pure modules like acl/query/capabilities/llm-contracts, repository manager etc): https://github.com/ppotepa/torrent-bot
---

*End of new-application.md*
