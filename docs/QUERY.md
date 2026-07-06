# Query Subsystem & Safe DSL

**Goal**: Allow both humans (via CLI) and the LLM to safely inspect the current state of the system (downloads, media, jobs, system info, etc.) without ever exposing raw SQL or internal databases.

## Why It Exists

In the old system the LLM could answer questions like:
- "are there any active downloads?"
- "show me the biggest files in the library"
- "what finished in the last 24h?"

It did this using a dedicated **Query agent + DSL**, not by dumping everything or letting the model write SQL.

This capability was very powerful and is explicitly called out as something to **evolve, not discard**.

## Core Concepts

### 1. Snapshot Sources (`ISnapshotSource`)

Plugins that have interesting state implement `ISnapshotSource`:

```csharp
public interface ISnapshotSource
{
    string Name { get; }                    // "downloads", "media", "jobs", "system.runtime"...
    QuerySourceMeta GetManifest();          // schema + examples + llm hints for the planner
    Task<object> GetSnapshotAsync(CancellationToken ct = default);
}
```

The `RepositoryAggregator` collects all of them.

### 2. QuerySourceMeta (what the LLM sees)

Each source describes itself richly:

- List of fields + types + allowed operators
- `llm_usage` guidance
- Example queries
- Default ordering, max limit, preconditions

This compact manifest is given to the Planner so it knows what it can ask.

### 3. QuerySpec — The Safe DSL

This is the contract the LLM (and CLI) produces:

```csharp
public record QuerySpec
{
    public string Source { get; init; } = "downloads";
    public List<QueryWhere> Where { get; init; } = [];
    public List<string> Select { get; init; } = [];
    public List<QueryOrderBy> OrderBy { get; init; } = [];
    public List<string> GroupBy { get; init; } = [];
    public List<Aggregate> Aggregate { get; init; } = [];
    public int Limit { get; init; } = 20;
}

public record QueryWhere(string Field, string Op, object? Value);
public record QueryOrderBy(string Field, string Direction = "asc");
```

Examples (as the LLM would generate):

```json
{
  "source": "downloads",
  "where": [
    { "field": "status", "op": "=", "value": "downloading" },
    { "field": "size", "op": ">", "value": 1073741824 }
  ],
  "select": ["name", "size", "progress", "eta"],
  "order_by": [{ "field": "size", "direction": "desc" }],
  "limit": 10
}
```

Supported operators (to be defined precisely in code): `=`, `!=`, `>`, `<`, `>=`, `<=`, `in`, `not in`, `contains`, `starts_with`, etc.

### 4. `query.execute` Capability

This is registered like any other capability.

The LLM Planner can include it in a plan when the user asks a question about current state.

It is also directly usable from CLI and (later) Telegram.

### 5. Execution

The query engine:
- Takes a `QuerySpec`
- Validates it against the source manifest (unknown fields, disallowed operators, limit violations → error)
- Executes it (currently planned: DuckDB in-memory against the snapshot rows)
- Returns `QueryResult` with items + count + summary + (optionally) the generated SQL for debugging

Important security rule (from the original system):

> Raw SQL is **never** accepted from the LLM. Only structured `QuerySpec`.

---

## How the LLM Uses Query

Flow (natural language path):

1. User: "czy są jakieś aktywne pobrania większe niż 5GB?"
2. Engine prepares:
   - Capability manifest
   - `repoSummary` = compact view of all registered sources (especially "downloads")
3. Planner sees the downloads source has `status`, `size`, `name`... and decides a `query.execute` step makes sense
4. Planner outputs a plan containing:
   ```json
   { "capability": "query.execute", "params": { "source": "downloads", "where": [...], ... } }
   ```
5. Executor validates the capability exists + ACL + risk
6. `query.execute` handler calls the query engine with the spec + current snapshot
7. Result is returned as part of the execution artifacts
8. Responder (or deterministic renderer) turns the result into a nice message

This is why the Planner **must** receive good repository summaries.

---

## Current State & Plans for .NET

### Keep / Port
- `QuerySpec` + related records (very close to the Python `contracts.py`)
- `QuerySourceMeta` + field descriptions
- The idea of a compiler/validator + engine
- DuckDB as the execution backend (via `DuckDB.NET` or similar)

### New / Improved
- Stronger typing where possible (while still allowing LLM-friendly JSON)
- Better separation between the query contracts and the aggregator
- `query.execute` as a proper capability with rich metadata
- Ability for plugins to declare query sources declaratively (attribute or registration)

### Possible Simplifications for First Version
- Start with in-memory evaluation (no DuckDB) for a few sources
- Add DuckDB once the flow is proven end-to-end
- Or use DuckDB from day one (recommended if we want real power quickly)

---

## Files / Modules (Planned)

```
src/
  Contracts/Query/
    QuerySpec.cs
    QueryWhere.cs
    QueryResult.cs
    QuerySourceMeta.cs
    ...

  Engine/Repositories/
    RepositoryAggregator.cs
    ...

  (later)
  Query/
    IQueryEngine.cs
    DuckDbQueryEngine.cs
    SpecValidator.cs
    ...
```

The `query.execute` capability will live in a small `Query` plugin or inside the System plugin initially.

---

## Examples of Useful Queries (for docs & tests)

- All currently downloading torrents
- Largest 10 files in the library
- Downloads that finished in the last 24 hours
- Jobs that failed
- Media files without matching torrent record (orphans)

These become both human-useful and LLM-useful.

---

See also:
- [ARCHITECTURE.md](ARCHITECTURE.md) (section on Repositories and LLM)
- `docs/ENGINE.md`
- Old reference implementation in `src/query/` of the Python project (contracts + compiler + engine)
