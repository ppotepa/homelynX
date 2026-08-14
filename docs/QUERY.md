# Query

Homelynx contains a structured, read-only query subsystem backed by DuckDB.

The Query plugin exposes `query.execute`. It accepts a validated query specification and runs it against registered snapshot sources. This functionality is independent of Telegram command interpretation and is retained because it provides useful diagnostics and automation over structured runtime data.

## Sources

Registered snapshot sources can include downloads, jobs, media files and system runtime state. Sources expose a defined schema and are aggregated by the repository/query layer.

## Safety

`DuckDbQueryCompiler` validates the requested source, filters, ordering and limits before producing the SQL executed by `DuckDbQueryEngine`. Arbitrary command execution is not part of this interface.

Query configuration uses the `BOT_QUERY_*`/application query settings already exposed by the bootstrap. Those settings are not language-model configuration and should remain when the query capability is enabled.

## Usage

Use the explicit `query.execute` capability through the CLI or another trusted explicit caller. Query results are returned through the normal capability result and presentation path.
