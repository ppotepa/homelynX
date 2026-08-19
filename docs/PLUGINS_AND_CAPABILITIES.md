# Plugins and capabilities

Plugins register capability metadata, capability contracts and handlers with the engine registry. The registry is the source of truth for what the application can execute.

## Maintained plugins

- **Torrent** — search, list, pause, resume, delete/control, result selection and pagination.
- **Downloads** — search/start/start URL/list/cancel and downloader orchestration.
- **Jobs** — job listing and related runtime state.
- **Media** — media listing and TTS.
- **Query** — `query.execute` over structured snapshot sources.
- **System** — health, ping, status, help/capabilities, metrics and events.
- **BotControl** — bot lifecycle/control capabilities where configured.

## Capability metadata

Metadata describes operational properties: command alias, description, permission, risk, preconditions, long-running/read-only flags and scope. It does not contain language-model hints.

## Capability contracts

Contracts define exact semantics, parameters, user interaction requirements, response construction and deterministic continuation rules. Continuations are used for real workflows such as search → selection and confirmation-required operations.

## Adding a capability

1. Define metadata and, where needed, a contract.
2. Implement `ICapabilityHandler`.
3. Register both from the owning plugin.
4. Add explicit command routing if the capability is user-facing through Telegram.
5. Add ACL/risk/confirmation requirements and tests.

Do not add a second routing or planning layer around the registry; adapters should resolve explicit commands to registered capabilities and the engine should execute them directly.
