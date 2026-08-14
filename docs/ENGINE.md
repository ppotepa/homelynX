# Engine

`TorrentBot.Engine` executes already-resolved capabilities. It does not build or interpret execution plans.

## Invocation contract

An adapter creates an `Invocation` containing the explicit capability/command, parameters, user context, request context and optional dry-run/progress reporter. `InvocationPipeline` resolves the capability name where necessary and forwards the invocation to `EngineHost.SubmitAsync`.

`EngineHost` is responsible for:

1. capability resolution and registry lookup,
2. user/ACL checks,
3. confirmation requirements,
4. invoking the registered handler,
5. audit and execution result handling.

The pipeline then constructs response artifacts and registers any real pending application action, for example an indexed torrent selection after a search.

## Explicit input boundary

Telegram slash commands and the explicit CLI command surface are accepted. Ordinary Telegram text is not interpreted as a command. Callback data from rendered Telegram buttons is accepted for explicit selections and confirmations.

## Capabilities

Capabilities are registered by plugins. Metadata describes command, permission, risk, scope, read-only/long-running properties and concrete preconditions. Capability contracts describe parameters, response construction and continuation rules required by deterministic application workflows.

## State and confirmations

`ConversationContextStore` stores per-session pending actions and search/selection context. `ConfirmationStore` protects operations that require explicit confirmation. These stores are application workflow state; they are not prompt or language context.

## Jobs and events

Long-running downloads are tracked by the jobs subsystem. The internal event bus/outbox remains available where lifecycle and completion events are actually consumed.
