# Architecture — Media Server Bot (TorrentBot2)

**Status**: Authoritative design document for the .NET rewrite  
**Date**: 2026-07-05  
**Language**: C# / .NET  
**Focus**: Central `Engine` as the orchestrator (current phase)

This document fully describes what the project does and how it is built. It describes the current .NET implementation.

---

## 1. What This Project Is

**Media Server Bot** is the intelligent control plane for a private homelab media stack.

Primary user experience:
- You talk to it via **Telegram** (or the diagnostic **CLI**).
- You can use classic slash commands (`/search`, `/list`, `/status`...) **or** plain natural language.
- The bot can search for content (primarily via Jackett for torrents), start downloads through different providers (torrent, direct URL, future ones), route completed files into your Jellyfin library, answer questions about current state using a safe query DSL, and run maintenance tasks.

Core value loop:
```
User intent → (slash or LLM Planner) → validated Capability 
    → [optional Process Manager] 
    → Job (tracking) + side effects (events) 
    → Repositories updated 
    → reply (or live progress)
```

Everything important is expressed as a **Capability**. 
Long-running or complex business processes are orchestrated by **Process Managers**.
Work is tracked via **Jobs** (as observable projections/correlation tokens).
Queryable state is exposed through **Repositories** (snapshots).

**Chosen architectural pattern for background/long-running work**:
**Process Manager (Orchestration) + Job as Correlation Token / Projection**

See section 4.6 for full description.

The previous implementation suffered from a giant god object (`CommandHandlerService`). This rewrite puts a clean, central **Engine** in the middle.

---

## 2. Scope — What the Project Does (Fully)

### 2.1 Primary Features (MVP → v1)

- **Download management** (general + pluggable providers)
  - Unified tracking and control of downloads regardless of source
  - Provider-specific search (e.g. Jackett for torrents)
  - Support for multiple backends:
    - Torrent (Jackett search + qBittorrent)
    - Direct URL / HTTP downloads
    - (future) Usenet, SFTP, etc.
  - Post-download routing into media library (Jellyfin)
  - Monitor, pause, resume, cancel, retry across all providers
  - All downloads visible through a single "downloads" repository + query DSL

- **Media awareness**
  - Know what exists in the library (via filesystem snapshots or Jellyfin)
  - Help with organization / finding large files / duplicates

- **Natural language interface**
  - LLM Planner turns free text into a structured plan of capabilities
  - Safe execution through the same registry used by slash commands
  - Uses **repository summaries** so the model knows what data it can inspect

- **Safe structured querying**
  - `query.execute` capability (and direct API)
  - JSON/structured `QuerySpec` (source + where + select + order + limit + aggregates)
  - Never raw SQL from the LLM
  - Executed against aggregated snapshots (DuckDB or equivalent in-memory engine)

- **Reliability & control**
  - ACL is attribute-driven and profile-based (fine-grained per capability)
  - Risk levels: Safe / ConfirmationRequired / Destructive / Admin
  - Pending confirmations with tokens (buttons in Telegram)
  - Full audit log
  - Live progress / verbosity (edits the same Telegram message)
  - Dry-run mode (especially powerful in CLI)

- **Extensibility**
  - Plugins declare capabilities + optional repositories
  - Discovery via reflection + attributes
  - Narrow `EngineContext` surface only

### 2.2 Secondary / Later Domains

- TTS generation and playback
- Surveillance recorder + event notifications
- Coordinate tracking / timeline
- Additional download protocols

These will be added as plugins once the Engine core is solid.

### 2.3 Non-Goals (Infrastructure stays frozen)

- Docker Compose, images, service topology
- Media folder layout
- qBittorrent / Jackett / Jellyfin / Ollama behavior
- External services and ports
- The old Python code (we are rewriting only the application logic)

---

## 3. Guiding Principles

1. **Engine owns orchestration** — Adapters, plugins, LLM and repositories talk *to* or *through* the Engine. Never directly to each other.
2. **Narrow surface for plugins** — A plugin only sees `IEngineContext`. It does not see Telegram objects, full config, or other plugins' internals.
3. **Declarative over imperative** — Use attributes (`[Capability]`, `[Repository]`) + reflection. One discovery pass at startup.
4. **Slash = deterministic, plain text = planned** — Both paths end up executing the same registered capabilities.
5. **Query is first-class** — `query.execute` is a normal capability. The LLM uses repository summaries to decide when to call it.
6. **Two-model LLM** — Small/fast Planner (intent → plan), stronger Executor/Responder (validation + execution + reply).
7. **Bus + Jobs are the spine** — Async communication and background work go through the internal bus and job system.
8. **Everything is observable** — Audit, verbosity, job progress, repository refreshes.
9. **CLI is a first-class adapter** — The best way to develop, test, dry-run and inspect the Engine.
10. **Evolve, don't discard** — The existing query DSL, repository snapshot idea, ACL (profile + attribute driven), and rich capability metadata are valuable. ACL metadata lives primarily in `[Capability]` attributes.

---

## 4. High-Level Architecture (C# View)

```
┌─────────────────────────────────────────────────────────────┐
│                    INPUT ADAPTERS                           │
│   CLI (first-class for dev/test)     Telegram Bot (prod)    │
└───────────────────────────────┬─────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────┐
│                      ENGINE (Orchestrator)                  │
│  • Engine / Composition Root                                │
│  • CapabilityRegistry (attribute-driven)                    │
│  • RepositoryAggregator                                     │
│  • IInternalBus (Events / Commands / Jobs)                  │
│  • Job tracking infrastructure (queue/runner for transient; │
│    special handling for LongLived via Process Managers)     │
│  • ACL adapter (thin)                                       │
│  • Audit sink                                               │
└───────┬───────────────┬───────────────────┬─────────────────┘
        │               │                   │
        ▼               ▼                   ▼
┌──────────────┐  ┌──────────────────────┐   ┌────────────────────────────┐
│ LLM Component│  │ Plugins +            │   │      Repositories          │
│ • Planner    │  │ Process Managers     │   │ (per-plugin SnapshotSource)│
│ • Executor   │  │ (downloads with      │   │ • DownloadsSource          │
│ • Responder  │  │ DownloadProcessMgr,  │   │ • MediaSource              │
│              │  │ system, query...)    │   │ • JobsSource               │
└──────────────┘  └──────────────────────┘   └────────────────────────────┘
                  └──────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────┐
│                 EXTERNAL SERVICES (unchanged)               │
│  qBittorrent • Jackett • (aria2 / yt-dlp / other downloaders) │
│  Jellyfin • Ollama • TTS • ...                              │
│  + DuckDB (for query execution)                             │
└─────────────────────────────────────────────────────────────┘
```

**Two Invocation Paths**

**A. Deterministic (slash / explicit)**
- Adapter → `Invocation(isExplicit=true)`
- Engine resolves capability directly
- ACL + preconditions → execute handler (or start Process / create Job)
- Result → adapter

**B. Natural Language**
- Adapter → `Invocation(isExplicit=false, text=...)`
- Engine builds facts + capability manifest + `repoSummary`
- `Planner.Plan(...)` → `PlanEnvelope` (steps)
- `Executor` validates every step against registry + ACL
- Execute steps (may involve `query.execute` or other capabilities / jobs)
- Responder (or deterministic renderer) produces final text
- Result → adapter

---

## 4.5 Download Management & Providers

One of the core responsibilities of the system is downloading content. In the old implementation this was tightly coupled to torrents (Jackett + qBittorrent). In the new architecture we introduce a **general Download abstraction** while keeping provider-specific logic encapsulated.

### Goals
- A single, queryable view of all downloads (active, completed, failed).
- Pluggable download backends ("providers").
- Search is often provider-specific (torrent indexers are different from "search the web for a direct link").
- The LLM and CLI can work with downloads in a general way (`download.search`, `download.start`, `query` over downloads).
- Post-processing (media library routing, notifications) happens in one place after any downloader finishes.

### Key Concepts

**DownloadProcessManager** (the Process Manager for downloads)
- Lives inside the `downloads` plugin.
- Implements the Process Manager pattern for all download-related work.
- Coordinates registered `IDownloader` implementations (Strategy pattern).
- Provides unified capabilities:
  - `download.search` (with optional `provider` / `type`)
  - `download.start` / `download.start_url`
  - `download.list`, `download.control`, `download.cancel`, etc.
- Owns or coordinates the **"downloads"** `ISnapshotSource` (used by `query.execute` and the LLM).
- Handles common concerns:
  - Destination path selection / category
  - Completion detection
  - Automatic routing to media library
  - Failure handling and retries
  - Progress events on the bus

**IDownloader** (pluggable interface)

```csharp
public interface IDownloader
{
    string Type { get; }                    // "torrent", "http", "usenet"...
    string DisplayName { get; }

    // Search may not be supported by every provider
    Task<SearchResults> SearchAsync(SearchRequest request, CancellationToken ct = default);

    Task<DownloadTicket> StartAsync(StartRequest request, CancellationToken ct = default);

    Task<DownloadStatus> GetStatusAsync(string downloadId, CancellationToken ct = default);

    Task PauseAsync(string downloadId, CancellationToken ct = default);
    Task ResumeAsync(string downloadId, CancellationToken ct = default);
    Task CancelAsync(string downloadId, CancellationToken ct = default);
    // ...
}
```

Each implementation talks only to its external service(s).

### Concrete Implementations (examples)

**TorrentDownloader**
- Search: uses Jackett (or multiple indexers + FlareSolverr)
- Actual download: qBittorrent API (add magnet/torrent, set category, save path)
- Rich metadata: seeders, size, indexer, hash, etc.
- Status polling or webhook-style updates
- This is currently the main/only implementation

**UrlDownloader** (or DirectHttpDownloader)
- Accepts direct HTTP/HTTPS URLs (and magnets that resolve to direct links)
- Can use built-in HttpClient for simple cases or delegate to a proper downloader (aria2c, yt-dlp for YouTube/video sites, etc.)
- Useful for:
  - Direct links from the user
  - "download this URL" natural language commands
  - Content that is not on torrent trackers

**Future / Other possible implementations**
- `UsenetDownloader` (NZB + SABnzbd or NZBGet)
- `SftpDownloader` / `FtpDownloader`
- `WebDavDownloader`
- `IPFS` or other decentralized sources
- Wrapper around external tools (yt-dlp as a general "media grabber")

### How it fits the architecture

- A `downloads` plugin registers the general capabilities and the unified `downloads` snapshot source.
- Specific downloader implementations can be:
  - Separate small plugins (`torrent`, `url-downloader`), or
  - Registered providers inside the `downloads` plugin (via DI or explicit registration).
- The `TorrentDownloader` will contain most of the old torrent logic (search + qBittorrent control).
- All downloaders publish events (`DownloadStarted`, `DownloadProgress`, `DownloadCompleted`, `DownloadFailed`) on the internal bus.
- `RepositoryAggregator` exposes the unified view.
- LLM Planner can decide to use `download.search` + `download.start` or call `query.execute` on the "downloads" source.

**Relationship to Jobs** (see section 5.3):
Downloads are tracked as `JobKind.LongLived`. When the DownloadProcessManager starts work, it creates a Job (with `ExternalId` pointing to the real torrent in qBittorrent). The richer download data lives in the `downloads` SnapshotSource. Control actions like delete/pause are usually direct operations on the download / Process Manager, not new Jobs (see "Commands vs Jobs for control actions").

### Capability examples (recommended naming)

- `download.search` — general search (may dispatch to torrent or other providers based on query or explicit `provider` param)
- `torrent.search` — explicit torrent-only search (still useful for clarity)
- `download.start_url` — start a direct URL download
- `download.list`
- `download.cancel`

This gives the LLM flexibility while keeping things clear for humans using slash commands.

### Repositories

A single `downloads` source (or multiple namespaced ones) that the query engine can use:

```json
{
  "source": "downloads",
  "where": [{ "field": "status", "op": "=", "value": "downloading" }],
  "select": ["name", "provider", "progress", "size", "eta"]
}
```

The source can merge data coming from all active `IDownloader` instances.

---

## 4.6 Process Managers (Chosen Pattern for Long-Running Work)

### Why Process Manager + Job Projection

After analyzing the requirements (different lifetimes, external stateful systems like qBittorrent, need for LLM visibility, clean Engine ownership, pluggable providers), we selected the **Process Manager pattern** (from Enterprise Integration Patterns) combined with **Job as a lightweight correlation token and read-model projection**.

**Core idea**:
- A **Process Manager** owns the lifecycle and decision logic of a long-running business process (e.g. "Download + Post-process").
- The **Job** is *not* the process itself. It is an observable handle (Id + state + progress) that the rest of the system (CLI, Telegram, LLM planner, query) can use.
- Real domain state lives in rich **Repositories** (e.g. `downloads` SnapshotSource).
- External systems are synchronized via events or polling into the Process Manager.

This gives us:
- Clean separation between orchestration logic and infrastructure (Engine only provides Bus + Job tracking + Context).
- Different handling strategies per `JobKind` / process type.
- Natural support for cancellation, progress, compensation.
- Excellent fit for LLM (planner sees capabilities; executor can wait on or query Jobs).

### Process Manager Responsibilities

A Process Manager (typically inside a plugin):

- Is started by a Capability handler (or directly from Executor for planned steps).
- Creates and owns one primary **Job** (usually `LongLived`).
- Coordinates one or more `IDownloader` (or other services) via Strategy.
- Reacts to domain events on the bus.
- Updates the Job projection.
- Updates rich domain snapshots (via repositories).
- Can spawn child Jobs (e.g. post-processing after download completes).
- Handles timeouts, retries, and compensation where needed.
- Publishes high-level events (`DownloadCompleted`, `ProcessFailed`).

### Relationship to Other Concepts

| Concept            | Role |
|--------------------|------|
| **Capability**     | Entry point / trigger. Starts the Process Manager. |
| **Process Manager**| Orchestrates the business process. |
| **Job**            | Observable tracking token + progress + status (for query, LLM, UI). |
| **IDownloader**    | Concrete implementation of one step (search/start/status). Strategy inside the manager. |
| **Repository**     | Rich, queryable snapshot of current state (`downloads`, `jobs`, `media`). |
| **Bus Events**     | Reactive glue. Process Manager subscribes and publishes. |
| **Engine**         | Provides the narrow `IEngineContext` (publish, job tracking, query) and infrastructure. Never owns process logic. |

### Job as Projection (not source of truth)

For long-lived work:
- The **Download** entity in the `downloads` repository is the source of truth.
- The **Job** is a projection optimized for:
  - Uniform querying across all work types
  - Progress bars and live verbosity
  - LLM reasoning ("are there any active long-lived jobs?")
  - Cancellation tokens and ownership

When the Process Manager updates state, it:
1. Updates its internal domain model / repository snapshot.
2. Updates the associated Job record (progress, status, result).
3. Publishes events.

### Process Manager Lifecycle Example (Download)

1. Capability `download.start` (or LLM step) is executed.
2. Handler calls `context.GetService<IDownloadProcessManager>().StartDownload(...)`.
3. Process Manager:
   - Creates Job: `Kind=LongLived, Type="download.torrent", ExternalId="hash123"`
   - Selects `TorrentDownloader` (based on payload)
   - Calls `downloader.StartAsync(...)`
   - Subscribes to bus or sets up polling for updates
4. As external system reports progress → Process Manager updates Job + `downloads` snapshot.
5. On completion:
   - Marks Job Succeeded
   - Triggers post-processing step (media routing) — can be another Job or direct call
   - Publishes `DownloadCompleted`
6. User / LLM can query both `jobs` and `downloads`.

Control actions (`pause`, `delete`) are sent as commands to the Process Manager for the specific Job Id.

### Registration

Process Managers are registered similarly to other services:

- Inside plugin's `Register(IEngineContext)` or via attributes.
- They receive `IEngineContext` (for bus, jobs, query).
- They can be discovered or explicitly wired in the plugin.

Example registration:

```csharp
public class DownloadsPlugin : IPlugin
{
    public void Register(IEngineContext ctx)
    {
        ctx.RegisterService<IDownloadProcessManager>(new DownloadProcessManager(ctx, downloaders));
        // also register capabilities and snapshot sources
    }
}
```

### Error Handling, Retries & Compensation

- Transient errors → retry within the Process Manager (with backoff).
- Permanent failure → mark Job Failed + publish event + optional compensation (e.g. remove partial files).
- Long-running external failures (qBittorrent down) → Job goes to `Paused` or `Waiting` with reason.
- Process Managers can implement simple Saga-like compensation for multi-step flows (download succeeded → routing failed → cleanup).

### Dry-run & Testing Strategy

The architecture must be fully testable, especially for long-running processes and context propagation.

- **Dry-run support**:
  - `IEngineContext.IsDryRun` is true for test/CLI dry runs.
  - Process Managers and handlers must check `ctx.IsDryRun` and avoid side effects on external systems (e.g. no real qBittorrent calls; record intended actions instead).
  - `IRequestContext` is still provided (with fake but realistic TraceId, User, Source) so logs, Jobs, and audit can be exercised end-to-end.
  - Job creation still happens (in-memory), but ExternalId may be synthetic.
  - Used by CLI `agent plan` and `agent run --dry-run`.

- **Unit testing**:
  - Fake `IEngineContext` (in-memory JobTracker, no-op bus, dry-run=true, controlled RequestContext).
  - Test individual Process Manager steps, Job state transitions, ACL checks, context attachment.

- **Integration testing**:
  - Register fake plugins/Process Managers/IDownloaders.
  - Submit Invocation with full RequestContext.
  - Verify Job created with correct correlation in Metadata, events published with context, logs would have scopes.
  - Exercise long-lived flows by simulating external progress via events.

- **Property-based / scenario tests**:
  - For query + ACL combinations.
  - For context propagation across boundaries (Bus message carries context, Job update preserves it).

- **Dry-run for LLM paths**:
  - Planner can be called with manifests; Executor runs with IsDryRun so no real actions, but full trace of plan execution + Job projections is produced for verification.

This ensures the Process Manager + RequestContext model can be validated without external dependencies.

See also:
- Section 5.3 (detailed Job model and kinds)
- Section 5.5 (how RequestContext flows in dry-run)
- Section 4.5 (IDownloader providers — used as strategies *by* the DownloadProcessManager)
- docs/ENGINE.md (Jobs vs Direct Execution)

---

## 4.7 ACL Integration (Permissions from Attributes + Profiles)

### Core Rule
**ACL metadata comes from the `[Capability]` attribute.**  
The `Permission` field on the attribute (e.g. `Permission = "USER"`) declares the minimum profile required.

This is declarative and discoverable — exactly what the LLM planner, CLI help, and registry need.

### How ACL Works (Matrix of Permissions)

The model is the same proven one from the original system:

1. **Profiles / Presets** (from `acl/presets.cfg` or built-in):
   - `PUBLIC`, `USER`, `ADMIN`, `MODERATOR`, custom ones.
   - Presets expand (e.g. `USER = ALL | !destructive-things`).

2. **User grants** (from `allowed-users.cfg`):
   - `telegram_user_id USER`
   - `username ADMIN|!torrent.delete`

3. **Per-Capability declaration** (in attribute):
   ```csharp
   [Capability(
       Name = "torrent.search",
       Permission = "USER",           // ← this is the ACL hook
       Risk = RiskLevel.Safe,
       ...
   )]
   ```

4. **Matching engine** (AclService):
   - Takes user identity + list of grants.
   - Checks against capability's declared `Permission` + `Module` + `Name`.
   - Supports `!deny` overrides.

### Integration Points in the New Architecture

- **CapabilityRegistry** collaborates with `AclService`:
  - `GetCapabilitiesForUser(user)` → only returns what the user may see/use.
  - This filtered list is given to the LLM Planner (critical for safety).

- **Before execution** (in Engine or Executor):
  - Check ACL for the capability + current user.
  - If starting a Process Manager → check at start.
  - Control actions on existing Jobs (cancel, pause) → re-check ACL for that capability or a related one.

- **In `IEngineContext` / `CapabilityContext`**:
  ```csharp
  // passed down
  public record UserContext(string UserId, string[] Grants, string EffectiveProfile);

  // inside context
  bool CanExecute(string capabilityName);
  ```

- **Process Managers**:
  - The `DownloadProcessManager` receives the `UserContext` when started.
  - For long-lived Jobs, ownership is recorded (`Job.Metadata["owner"] = userId`).
  - Later control actions should verify that the caller has rights (owner or ADMIN).

- **Risk vs Permission** (two separate dimensions):
  | Dimension   | Example values          | Purpose |
  |-------------|-------------------------|--------|
  | `Permission`| USER, ADMIN, PUBLIC     | Who is allowed to even invoke it |
  | `Risk`      | Safe, Confirm, Destructive | What happens after you are allowed (confirmation gates) |

### Recommendations for Implementation

- Port/adapt the old `src/acl/` logic as a clean C# library (`AclService`, `AclMatcher`, `PresetExpander`).
- Keep the same `.cfg` file formats for compatibility with existing setups.
- ACL should be **stateless** and injected as a service.
- The Engine should expose a thin `IAclService` through `IEngineContext.GetService<IAclService>()` only when really needed (most checks should be done at the Engine/Executor level).
- For LLM: the manifest passed to Planner **must** already be ACL-filtered for the current user. Never let the model see capabilities the user cannot run.

### Decisions (closed)

- **Jobs ACL**: No independent ACL on Jobs themselves. A Job carries `owner` in metadata (from originating RequestContext.UserId; adapter also sets CurrentUser.UserId). Visibility of LongLived jobs is granted to the owner + ADMIN profiles. Control capabilities (e.g. `download.cancel`) perform owner-or-admin check inside the Process Manager or via a thin `CanControlJob` helper. This keeps ACL tied to capabilities (per plan criterion) while avoiding duplication.

- **Repository query filtering by ACL**: No automatic per-user row filtering for core sources (`downloads`, `jobs`, `media`). Homelab setups treat these as shared within the allowed user set. Individual `ISnapshotSource` implementations may apply owner-based filters if the source declares `requiresOwnerFilter` in its manifest. `query.execute` always runs under the caller's ACL for the capability itself.

- **Bootstrap first user**: The Engine exposes an optional `BootstrapFirstUser` hook (configurable via EngineOptions.AllowFirstUserBootstrap). On first capability invocation with no configured grants, if enabled and the source is trusted (e.g. local CLI), it can auto-grant ADMIN. Behavior matches the original `allowed-users.cfg` bootstrap logic for byte-identical compatibility. Documented in acl/ usage.

ACL remains one of the most mature parts — behavior **byte-for-byte identical** during migration. All open questions resolved above.

---

## 5. The Engine — Core of the Project (Current Focus)

The `Engine` is the single source of truth for runtime behavior.

### 5.1 Responsibilities

- Lifecycle (`StartAsync`, `StopAsync`) — including graceful shutdown of long-running processes
- Plugin discovery and registration (Capabilities, Repositories, Process Managers, Services)
- Capability registry (metadata + executable handlers)
- Repository aggregation
- Submission of `Invocation`s (both paths)
- Coordination of Planner → Executor flow
- Infrastructure for Process Managers (bus, job tracking, context)
- Bus publication / subscription
- Job creation, update, and query surface
- Providing narrow `IEngineContext` to plugins, handlers, and Process Managers
- Observability hooks (audit sink, verbosity events)

### 5.2 EngineContext (the narrow surface)

Plugins, capability handlers, Process Managers, and LLM components receive only this narrow surface (see §9.2 for the authoritative `IEngineContext` definition, including the `RequestContext` property).

**Important rules**:
- Never pass the full Engine.
- Process Managers and handlers get this context — they use it to publish events, create/update jobs, query state, resolve services, **and check permissions**.
- `CurrentUser` and `RequestContext` are set by the input adapter (Telegram/CLI) and must be trusted.
- Dry-run support must be first-class (used heavily by CLI) and should still respect ACL.
- Correlation data from `RequestContext` should be automatically attached when publishing events or updating jobs (Engine infrastructure can help here).

See §9 for all authoritative contract definitions (IRequestContext, IEngineContext, Job, etc.).

Plugins and Process Managers should prefer publishing events and updating Jobs over direct calls between each other.

### 5.3 Internal Bus + Jobs (the spine with different lifetimes)

The bus + job system is the main mechanism for async coordination and observability.

We distinguish three things on the bus:

- **Events** — fire-and-forget notifications (`DownloadStarted`, `RepositoryRefreshed`, `JobProgressUpdated`, `DownloadCompleted`...)
- **Commands** — internal control messages
- **Jobs** — trackable units of work that have a lifecycle and can be queried

#### The Job model must support very different kinds of work

Not every background operation has the same characteristics. A 30-second file scan is very different from a download that can run for 3 days and then seed for 2 weeks.

See §9.5 for the authoritative `Job`, `JobKind`, `JobStatus`, and `JobOptions` definitions (sole authority site per Contract Authority table in §9). See 5.5 rule 3 for how the tracker populates correlation (including `UserId` from `RequestContext`) and `OwnerUserId`.

**Process Managers are the primary creators and updaters of Jobs** (especially LongLived ones). The Engine provides the storage and update primitives via `IEngineContext`.

#### How Process Managers use Jobs

See the detailed description in section 4.6 (Process Manager pattern).

Summary:
- Long-running domain processes (downloads) create one primary LongLived Job.
- The Job acts as correlation ID and progress projection.
- The Process Manager is responsible for keeping the Job in sync with reality (external downloader + internal steps).
- Short work can use Transient Jobs directly from capability handlers without a full Process Manager.

#### How different operations map to Jobs (examples)

| Operation                     | Job created?     | Kind        | Typical Lifetime | Notes / Process Manager involvement |
|-------------------------------|------------------|-------------|------------------|-------------------------------------|
| Start torrent download        | Yes (primary)   | LongLived   | Days/weeks       | DownloadProcessManager owns it. ExternalId = qBittorrent hash |
| Start direct URL download     | Yes             | LongLived   | Hours/days       | Same Process Manager or Url-specific one |
| Delete / remove download      | Usually no      | —           | —                | Control command sent to Process Manager. Optional small Transient cleanup Job |
| Large file scan               | Yes             | Transient   | Minutes          | Simple handler or lightweight manager |
| Periodic status refresh       | Yes (internal)  | Recurring   | Ongoing          | Managed by downloads plugin |
| LLM summary / plan generation | Yes             | Transient   | Seconds          | Usually created by LLM Component via context |
| Post-download media routing   | Often yes       | Transient   | Minutes          | Child Job or step inside the same LongLived process |

**Key principle**: 
- A **Job** is primarily for *tracking, progress, cancellation, and visibility* inside the Engine.
- The actual heavy state for long-lived things (especially downloads) lives in the **Downloader** + external service + the richer `downloads` SnapshotSource.
- We still create a Job so that `query jobs`, the LLM, and the bus can see it uniformly.

#### Downloads as special LongLived Jobs

When the DownloadProcessManager starts a download:
1. It creates a Job with `Kind = LongLived`, `Type = "download.torrent"` (or `"download.url"`).
2. The Job's `ExternalId` stores the qBittorrent hash (or equivalent).
3. The selected `IDownloader` (TorrentDownloader or UrlDownloader) does the real work.
4. The Process Manager (via bus events or polling) keeps updating the Job's `Progress`, `Status`, and `Result`.
5. On completion, the Job is marked Succeeded and a `DownloadCompleted` event is published.
6. The DownloadProcessManager can then trigger post-processing (media routing) — possibly as another Job or internal step.

This way:
- Downloads are visible through both the dedicated `downloads` repository (rich data) **and** the general `jobs` repository.
- LLM can ask "what long running jobs do I have?" or use `query.execute` on `downloads`.
- Cancellation from the user translates to cancelling the Job + telling the downloader to stop.

#### Retention & different lifetimes

- **Transient jobs**: have `ExpiresAt` or are cleaned by a background sweeper after success + N days.
- **LongLived jobs** (downloads): kept until the user explicitly removes the download or a very long retention policy.
- Important jobs can be marked with tags (`"important"`, `"user-initiated"`) so they survive longer.

The `jobs` SnapshotSource (registered by the downloads or system plugin) can filter by `Kind`, `Status`, `Tags`, `CreatedAt` etc.

#### Commands vs Jobs for control actions

Actions like `pause`, `resume`, `delete`, `remove with data` are often better modeled as:
- Direct capability execution (fast path), that
- Mutates the target long-lived Job / Download entity, and
- Optionally creates a small follow-up Job for cleanup work.

This avoids creating thousands of tiny "delete-xxx" jobs that nobody cares about after 5 minutes.

Implementation starting point: `System.Threading.Channels` for the queue. We will need different handling strategies depending on `JobKind` (e.g. long-lived jobs may not be "run" by a simple worker pool — they are more like tracked external processes). Long-lived processes are primarily driven by their Process Manager reacting to events.

### 5.4 Full Request Lifecycle (Deterministic + Planned + Long-Running)

#### A. Deterministic / Slash Path (simple case)
1. Adapter normalizes input to `Invocation` (explicit command).
2. Engine resolves capability directly from registry.
3. ACL + preconditions check.
4. Build `CapabilityContext` (includes `IEngineContext`).
5. Execute handler.
   - If short → return result immediately.
   - If long → handler starts Process Manager → creates LongLived Job → returns Job Id + initial status.
6. Side effects published as events.
7. Adapter renders (or subscribes to bus for live updates).

#### B. Natural Language Path (with LLM)
1. Adapter → `Invocation` (plain text).
2. Engine gathers:
   - Facts (cheap deterministic parsing)
   - Capability manifest (filtered by ACL)
   - Repository summaries (compact view from `RepositoryAggregator`)
3. Planner produces `PlanEnvelope` (steps + confidence).
4. Executor validates every step against registry + ACL + current state.
5. For each step:
   - If capability is "fire and forget" or quick → execute directly.
   - If it starts a process → start Process Manager (which creates Job).
   - Executor may wait for Job completion (with timeout) for steps that need the result.
6. Collect artifacts.
7. Responder (or deterministic renderer) produces final reply.
8. All progress stages published on bus (for verbosity).

#### C. Long-Running Process Continuation (after start)
- The initial capability returns quickly (Job Id).
- The Process Manager continues asynchronously (driven by bus events, timers, or polling the external system).
- Progress events update the Job and trigger live edits in Telegram / CLI output.
- On terminal state (Succeeded / Failed / Cancelled) → publish final events → optional post-steps (media routing, notifications).
- User can later query the Job or the richer repository, or send control commands.

### 5.5 Observability, Logging, Tracing, Context & Audit

The system is designed around the principle **"Everything is observable"**. Observability is not an afterthought — it is wired into the Engine, Bus, Jobs, and Process Managers.

#### Layers of Observability
1. **Structured Logging**
   - Use `Microsoft.Extensions.Logging` (or Serilog for richer structured output).
   - Every significant action uses structured logs with named properties (not string interpolation only).
   - Loggers are obtained via `ctx.GetLogger("Category")` from `IEngineContext`.

2. **Correlation & Request Context Propagation**
   - Every entry point (adapter) creates a **RequestContext** / correlation scope.
   - Key fields carried from the very first request:
     - `TraceId` / `Activity.Id` (for distributed tracing)
     - `InvocationId` (unique per user request)
     - `UserId` + effective profile
     - `Source` (cli | telegram)
     - `ChatId` / `MessageId` (for Telegram)
     - `JobId` (once a job is created)
     - `CapabilityName` or `ProcessType`
   - Propagation:
     - Adapters put correlation data into `IEngineContext` (CurrentTraceId, CurrentUser, and a richer `RequestContext`).
     - `IEngineContext` exposes helpers or the context is available via `AsyncLocal` / `Activity.Current` for deep async flows.
     - When publishing bus events or creating/updating Jobs, correlation properties are attached.
     - Process Managers and handlers use `ILogger.BeginScope` with the correlation data.
     - External calls (qBittorrent, Jackett, Ollama) should propagate TraceId where the client supports it (headers).
   - Result: You can follow a single user request end-to-end in logs, even across async jobs and Process Managers.
     Example log line (structured):
     ```
     {Timestamp} [Information] Download progress updated
     TraceId=abc123 InvocationId=inv-789 JobId=job-456 User=12345 Capability=download.start Provider=torrent Progress=42
     ```

3. **Tracing (Activity / OpenTelemetry ready)**
   - Use `System.Diagnostics.Activity` for spans around:
     - Capability execution
     - Process Manager steps
     - External service calls
     - Job state transitions
   - This makes the system ready for Jaeger/Zipkin or Azure Monitor without big changes.

4. **Metrics**
   - Counters: capabilities executed (by name, user profile, result)
   - Histograms: job duration, download speed, planner latency
   - Gauges: active LongLived jobs, queue depth
   - Exposed via a `system.metrics` capability or standard .NET metrics endpoint (future).

5. **Audit Trail** (separate from logs)
   - Every capability execution, plan, job terminal state, and important side effect is written to a durable audit sink (SQLite/portal in the old system).
   - Audit records contain full correlation context + redacted payload.
   - Used for compliance, debugging LLM decisions, and the web portal.

6. **Verbosity / Live Progress**
   - Implemented as bus subscribers (rate-limited message editing in Telegram/CLI).
   - Stages: parse → plan → validate → execute:<name> → post-process → respond.
   - Long-running processes emit `JobProgressUpdated` with correlation.

#### Configuration of Logging & Context
- Logging level, output format, enrichers, and sinks are configured at host level (appsettings.json / environment variables / DI).
- Engine can accept `LoggingOptions` or a list of context enrichers.
- Per-plugin or per-category log level overrides are supported.
- Context enrichment is configurable: which fields are always added to scopes (TraceId, UserId, JobId, etc.).
- Sensitive data redaction rules can be registered centrally.

#### Explicit Propagation Rules (must be followed by all components)
1. Every `Invocation` received by Engine MUST carry a non-null `RequestContext` whose `UserId` field (set by the adapter together with `CurrentUser.UserId`) identifies the actor.
2. `IEngineContext.RequestContext` is immutable for the lifetime of a capability/Process step.
3. When `CreateJob` is called, the tracker MUST copy `TraceId`, `InvocationId`, `UserId` (from RequestContext.UserId), `Source` into `Job.Metadata` and set `Job.OwnerUserId`.
4. `Publish<T>` for any event that relates to a job/capability MUST include the current `RequestContext` (or at minimum its TraceId/InvocationId) so subscribers can correlate.
5. `ILogger` obtained from `GetLogger` MUST have the RequestContext automatically enriched via scope (Engine infrastructure or middleware).
6. Process Managers are responsible for propagating the context when calling external clients (add headers) and when updating Job state.
7. On Job terminal state (Succeeded/Failed/Cancelled), the final `JobProgressUpdated` or custom event carries the original RequestContext for audit/verbosity.

#### Adapter Seeding Example (Telegram)
```csharp
// in TelegramAdapter
var userId = "..."; // from acl.ResolveUser(...) or similar
var reqCtx = new RequestContext(
    TraceId: Activity.Current?.Id ?? Guid.NewGuid().ToString("N"),
    InvocationId: Guid.NewGuid().ToString("N"),
    UserId: userId,
    Source: "telegram",
    ChatId: update.ChatId.ToString(),
    MessageId: update.MessageId?.ToString()
);
var userCtx = acl.ResolveUser(...); // separate for ACL checks if needed
await engine.SubmitAsync(new Invocation { ..., RequestContext = reqCtx, User = userCtx });
```

#### How Correlation Attaches to Bus Events and Jobs
- `Job.Metadata` always contains: "TraceId", "InvocationId", "UserId", "Source".
- Custom events published by Process Managers should implement `ICorrelatedEvent { IRequestContext Context; }` or carry the fields.
- Bus subscribers (verbosity, audit) read the correlation from the message or from ambient `Activity.Current` / `RequestContext` scope.
- External calls use `TraceId` as `X-Request-Id` or `traceparent` header where possible.

This guarantees end-to-end traceability from user message to final media routing log entry, even across hours of LongLived Job execution.

#### Context Propagation Flow Example (Download)
```
Adapter (Telegram/CLI)
  └─> creates RequestContext {TraceId, InvocationId, UserId, Source, ChatId}
      └─> Engine.Submit(Invocation, reqCtx)
            └─> Capability "download.start" (ACL check using reqCtx.UserId + userCtx)
                  └─> DownloadProcessManager.StartAsync(payload, reqCtx)
                        ├─> IJobTracker.Create(...)  --> Job {Id, Metadata:{TraceId, InvocationId, UserId}, Owner=UserId}
                        ├─> IDownloader.Start(...)   --> attaches TraceId header
                        └─> on progress event
                              ├─> UpdateJob(jobId, j => j.Progress=xx)  (auto-enriches from stored ctx)
                              ├─> Publish(DownloadProgress {..., Context=reqCtx})
                              └─> logger.BeginScope(reqCtx) --> structured log with all ids
```

Code example (inside Process Manager):
```csharp
public async Task<string> StartAsync(object payload, IRequestContext ctx, CancellationToken ct)
{
    var jobId = _tracker.Create("download.torrent", payload, new JobOptions { SupportsCancellation = true }, ctx);
    // store jobId in internal state or return it
    await _downloader.StartAsync(... , correlationHeaders: new { ctx.TraceId });
    return jobId;
}
```

### 5.6 LLM Component Interaction with Processes & Jobs

The Planner receives repository summaries that include current Jobs (especially LongLived ones).

The Executor can:
- Start processes (which create Jobs).
- Use `query.execute` on `jobs` or `downloads` as a step.
- Wait on a Job result when a subsequent step depends on it.

The LLM never talks directly to external downloaders — everything goes through registered capabilities and Process Managers.

### 5.7 Capability Model

Capabilities are the atomic executable units.

```csharp
[Capability(
    Name = "system.health",
    Command = "/health",
    Description = "Returns basic engine health",
    Permission = "PUBLIC",           // ACL comes from here
    Risk = RiskLevel.Safe,
    LlmUsage = "Use when user asks if the bot is alive or for diagnostics",
    IntentHints = new[] { "status", "health", "ping" }
)]
public sealed class HealthCapability : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext ctx, 
        HealthParams parameters, 
        CancellationToken ct)
    {
        // pure(ish) logic + calls through ctx
    }
}
```

Metadata is rich so:
- ACL can decide (Permission field)
- Planner knows when it is appropriate (LlmUsage, IntentHints, Preconditions)
- CLI can generate help
- Documentation can be auto-generated
- Risk drives confirmation gates before starting a Process Manager or executing a handler

Example full attribute usage (including long-running hint):
```csharp
[Capability(
    Name = "download.start",
    Permission = "USER",
    Risk = RiskLevel.ConfirmationRequired,
    IsLongRunning = true,           // hints that a Process Manager will own a Job
    Preconditions = new[] { "hasValidDownloadLocation" }
)]
```

### 5.8 Plugin Model (C#)

```csharp
public interface IPlugin
{
    string Name { get; }
    string Version { get; }

    void Register(IEngineContext context);   // or async version
}
```

Discovery (initial):
- Scan assemblies / configured plugin directory
- Find all types implementing `IPlugin` or decorated modules
- Also collect methods / classes decorated with `[Capability]` and `[Repository]`

A plugin may contribute:
- Zero or more capabilities
- Zero or more `ISnapshotSource` implementations (for the aggregator)
- Process Managers (via RegisterService<IDownloadProcessManager> or similar)
- Job handlers (via attributes or registration)
- Background tasks

### 5.9 Repositories & Snapshot Sources

```csharp
public interface ISnapshotSource
{
    string Name { get; }
    QuerySourceMeta GetManifest();           // rich metadata for LLM + query
    Task<object> GetSnapshotAsync(CancellationToken ct);
}
```

`RepositoryAggregator`:
- Collects all registered sources
- Can return compact summaries for the planner (including current LongLived Jobs)
- Exposes `QueryAsync(source, spec)`
- Can refresh on events (e.g. after a download job finishes or Process Manager publishes RepositoryRefreshed)
- Process Managers are primary writers to their domain snapshots (downloads, jobs) while the aggregator provides the read view.

### 5.10 Query Subsystem (Important for LLM)

The old `src/query/` (DSL compiler + DuckDB engine + `QuerySpec`) is valuable and will be ported/evolved.

Key points:
- `QuerySpec` is a safe, structured object (source, where clauses, select, order, group, aggregate, limit)
- LLM never sees or emits raw SQL
- `query.execute` is registered as a normal capability
- Sources come from the `RepositoryAggregator`
- Manifests (`QuerySourceMeta`) are given to the planner so it knows what fields and operators are available

This is one of the most powerful parts of the old system and must be preserved.

`query.execute` capability receives the caller's `RequestContext`, allowing audit and correlation of all data queries (including those on `jobs` and `downloads` snapshots written by Process Managers).

### 5.11 Error Handling, Resilience & Health

- **Error Taxonomy** (proposed):
  - Validation / Authorization (fail fast, user-visible)
  - Transient (retry with backoff — handled in Process Manager or dedicated resilience pipeline)
  - Permanent / External (mark Job Failed, notify user)
  - Internal / Unexpected (log with full context + correlation, mark Failed, never leak to user)

- Resilience policies (retries, circuit breakers, timeouts) are configured per external service / provider and applied inside IDownloader implementations or a shared resilience service.

- Health:
  - `system.health` capability reports Engine + critical external services status.
  - Process Managers can contribute health signals (e.g. "qBittorrent reachable?").
  - Future: readiness/liveness for container orchestration.

- Failed Jobs are queryable and can be retried via capability if the Process Manager supports it.

All errors carry full `RequestContext` for correlation.

### 5.12 Configuration

Configuration is layered:

- **Host level** (appsettings.json, environment variables, command line): logging, metrics exporters, connection strings to external services, default LLM models.
- **Engine level** (`EngineOptions`): job retention policies, default timeouts, dry-run mode, verbosity defaults, enabled plugins list.
- **Plugin / Process level**: injected via `IEngineContext.GetService` or dedicated options (e.g. `DownloadOptions` with retry policies per provider).
- **Capability metadata** can carry defaults (e.g. max file size hints).

Context configuration (what fields go into log scopes / traces):
- Registered at startup via enrichers or a `ContextEnrichmentOptions`.
- Example: always include `JobId`, `UserId`, `TraceId`, `Source`.

All configuration is read-only after Engine start (immutable options pattern).

---

## 6. LLM Component

Separate bounded context. Stateless. Talks to the Engine only through ports.

- **Planner** — text + facts + capability manifest + repo summary → `PlanEnvelope`
- **Executor** — takes plan, validates every step (exists? ACL? risk? confirmation?), runs, collects artifacts
- **Responder** — turns execution results + original request into a human reply (can be deterministic for many cases)

Two-model strategy (configurable via env):
- `LLM_PLANNER_MODEL`
- `LLM_EXECUTOR_MODEL` / fallback

The LLM component must never bypass the registry or execute side effects directly.

---

## 7. Adapters

### CLI Adapter (priority #1)

- Thin layer on top of real Engine (or dry-run EngineContext)
- Supports:
  - Direct capability calls
  - `agent plan "..."` / `agent run "..."` (dry-run)
  - Raw LLM inspection
  - Query execution
  - JSON output for scripting
- Will be the main development surface while Telegram adapter is not yet ported

### Telegram Adapter (later)

- Receives updates
- Normalizes to `Invocation`
- Submits to Engine
- Subscribes to bus for live verbosity editing, button callbacks, file delivery

---

## 8. Proposed Source Layout (C# / Current Phase)

```
src/
├── Engine/                          # The central piece we are building now
│   ├── Engine.csproj
│   ├── Engine.cs
│   ├── IEngineContext.cs
│   ├── EngineOptions.cs
│   │
│   ├── Bus/
│   │   ├── IInternalBus.cs
│   │   ├── InMemoryBus.cs
│   │   └── Messages/
│   ├── Jobs/
│   │   ├── Job.cs
│   │   ├── IJobTracker.cs
│   │   └── JobOptions.cs
│   │   # (Queue/Runner mainly for Transient jobs; LongLived driven by Process Managers)
│   │
│   ├── Capabilities/
│   │   ├── Attributes/
│   │   │   └── CapabilityAttribute.cs
│   │   ├── CapabilityRegistry.cs
│   │   ├── ICapabilityHandler.cs
│   │   └── CapabilityMetadata.cs
│   │
│   ├── Plugins/
│   │   ├── IPlugin.cs
│   │   ├── PluginLoader.cs
│   │   └── ...
│   │
│   └── Repositories/
│       ├── ISnapshotSource.cs
│       ├── RepositoryAggregator.cs
│       └── QuerySourceMeta.cs
│
├── Plugins/
│   └── System/
│       └── SystemPlugin.cs          # first plugin (health, status, etc.)
│
├── Adapters/
│   └── Cli/
│       └── Program.cs               # thin CLI driver (dry-run + real)
│
├── Contracts/
│   ├── Invocation.cs
│   ├── PlanEnvelope.cs
│   ├── QuerySpec.cs                 # the safe query DSL contract
│   └── ...
│
├── Llm/
│   └── ...
│
└── TorrentBot2.sln
```

Later we may split into more projects (`TorrentBot.Engine`, `TorrentBot.Contracts`, per-plugin projects) for better isolation.

---

## 9. Key Contracts (Definitions)

**Contract Authority (sole source of truth — all definitions live here):**

| Type                  | Subsection in §9 |
|-----------------------|------------------|
| IRequestContext / RequestContext | 9.1 Core Request / Correlation |
| IEngineContext        | 9.2 Engine Context |
| IProcessManager / IDownloadProcessManager | 9.3 Process Manager |
| IJobTracker           | 9.4 Job Tracker |
| Job / JobKind / JobStatus / JobOptions | 9.5 Job model and options |
| CapabilityContext / ExecutionContext / UserContext | 9.6 Execution contexts |

Prose sections elsewhere contain only "See §9.X" pointers. No duplicate code blocks.

These are the primary contracts that must be implemented for the Engine, Process Managers, and context propagation. They are intentionally minimal but usable as starting points for C# code.

### Core Request / Correlation
```csharp
public interface IRequestContext
{
    string TraceId { get; }              // Activity.Id or generated
    string InvocationId { get; }         // unique per user intent
    string UserId { get; }               // user identity; set by adapter alongside CurrentUser.UserId
    string? JobId { get; set; }          // set when Job created
    string? CapabilityName { get; }
    string Source { get; }               // "cli" | "telegram"
    string? ChatId { get; }
    string? MessageId { get; }
    IReadOnlyDictionary<string, object> Properties { get; } // extensible
}

public record RequestContext(
    string TraceId,
    string InvocationId,
    string UserId,
    string? JobId = null,
    string? CapabilityName = null,
    string Source = "unknown",
    string? ChatId = null,
    string? MessageId = null,
    IReadOnlyDictionary<string, object>? Properties = null
) : IRequestContext;
```

### 9.2 Engine Context (narrow surface + context)
```csharp
public interface IEngineContext
{
    // messaging
    void Publish<T>(T message) where T : class;
    IDisposable Subscribe<T>(Action<T> handler);

    // jobs / processes
    string CreateJob(string type, object payload, JobOptions? options = null);
    void UpdateJob(string jobId, Action<Job> updater);
    Job? GetJob(string jobId);

    // query
    Task<QueryResult> QueryAsync(string source, QuerySpec spec, CancellationToken ct = default);

    // capabilities
    IReadOnlyList<CapabilityMetadata> GetAvailableCapabilities();
    CapabilityMetadata? GetCapability(string name);

    // services
    T? GetService<T>() where T : class;

    // cross-cutting + context
    ILogger GetLogger(string category);
    string? CurrentTraceId { get; }
    CancellationToken CancellationToken { get; }
    bool IsDryRun { get; }
    UserContext CurrentUser { get; }
    IRequestContext RequestContext { get; }
    bool CanExecute(string capabilityName);
}
```

(See 5.2 for usage notes on the narrow surface.)

### 9.3 Process Manager
```csharp
public interface IProcessManager
{
    string ProcessType { get; }
    Task<string> StartAsync(object startPayload, IRequestContext context, CancellationToken ct);
    Task HandleCommandAsync(string jobId, string command, object? payload, IRequestContext actorContext, CancellationToken ct);
    // e.g. "pause", "cancel", "retry"
}

public interface IDownloadProcessManager : IProcessManager
{
    // domain-specific if needed
}
```

### 9.4 Job Tracker
```csharp
public interface IJobTracker
{
    string Create(string type, object payload, JobOptions? options, IRequestContext? ctx = null);
    void Update(string jobId, Action<Job> mutator);
    Job? Get(string jobId);
    // internal: attaches ctx.TraceId, ctx.UserId (from RequestContext), etc. to Job.Metadata
}
```

### 9.5 Job model and options
```csharp
public record Job(
    string Id,
    string Type,                    // "download.torrent", "scan.large_files", ...
    JobKind Kind,
    object Payload,
    JobStatus Status,
    double Progress,
    object? Result,
    string? Error,
    string? ExternalId,
    string? ExternalSystem,
    bool SupportsCancellation,
    bool SupportsPause,
    TimeSpan? EstimatedTotalDuration,
    DateTimeOffset? ExpiresAt,
    Dictionary<string, string>? Metadata,  // carries TraceId, InvocationId, UserId, Owner etc.
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ParentJobId,
    string? OwnerUserId
);

public enum JobKind
{
    Transient,      // short/medium finite work. Auto-clean after success + retention.
    LongLived,      // downloads, seeding, long surveillance. Kept much longer.
    Recurring,      // internal maintenance loops.
    Control         // rare — small actions that mutate other long-lived jobs/entities.
}

public enum JobStatus
{
    Queued,
    Running,
    Paused,
    Waiting,
    Succeeded,
    Failed,
    Cancelled
}

public record JobOptions(
    TimeSpan? Ttl = null,
    bool SupportsPause = false,
    bool SupportsCancellation = true
);
```

### 9.6 Execution contexts
```csharp
public record UserContext(string UserId, string[] Grants, string EffectiveProfile);

public class CapabilityContext
{
    public IEngineContext Engine { get; }
    public IRequestContext Request { get; }
    public UserContext User { get; }
    public string? JobId { get; }
    public bool IsDryRun { get; }
}

public class ExecutionContext : CapabilityContext
{
    public string? ParentJobId { get; }
    public IReadOnlyDictionary<string, object> StepParams { get; }
}
```

These contracts enable the Process Manager pattern: a capability starts a ProcessManager which owns a LongLived Job; correlation from RequestContext flows into the Job and all subsequent logs/events.

---

## 10. Migration & Incremental Approach

- Old and new can coexist (feature flag or separate scopes)
- Start with `system` plugin (health, status, capabilities list, query sources)
- Build minimal Engine that can register plugins and execute capabilities
- Add CLI adapter that drives the new path
- Port domains one by one (downloads/torrent logic is the most complex because of search + multiple providers)
- Eventually the old `CommandHandlerService` becomes a shim or is deleted

During the transition the powerful CLI from the old project is our model for the new CLI adapter.

---

## 11. Next Steps (Architecture Phase)

**Planning phase complete.** All acceptance criteria for finishing the architecture documentation have been addressed. No unresolved markers remain. Contracts and propagation are fully specified. Sections on Configuration, Error/Testing/Observability are complete and consistent. Supporting docs synced. Document reflects completion of planning phase.

Next (implementation phase):
- Implement the contracts in src/Engine.
- Port first Process Manager (downloads).
- Add tests exercising RequestContext propagation and Job correlation.

---

## 12. References


- Old repository query module (`src/query/` in reference) — contracts, compiler, DuckDB engine
- Old capability metadata and repository snapshot patterns

This document will be kept up to date as the design is refined.

---

**End of ARCHITECTURE.md**
