# E2E test plan

## Contract

Homelynx accepts explicit slash commands through Telegram and the corresponding explicit CLI command path. Free-form natural-language requests are out of scope and must not invoke capabilities.

## Core scenarios

| Area | Scenarios |
| --- | --- |
| System | `/help`, `/health`, `/status`, `/ping`, unknown slash command |
| Torrent | `/search <query>`, `/select <index>`, `/more`, `/torrents` |
| Downloads | `/download`, `/downloads`, `/cancel`, invalid URL/selection |
| Conversation | search creates pending selection; explicit selection/callback consumes it; confirmation callback works |
| Jobs | list and completion tracking |
| Media | media list and TTS where configured |
| Safety | ACL denial, confirmation-required operations, dry-run where supported |
| Input boundary | ordinary text without `/` does not execute a capability |

## Execution modes

Use deterministic/fake-backed tests for routing and state transitions. Use `--live` only for behavior that genuinely depends on Telegram, Jackett, qBittorrent, Jellyfin/media paths or TTS.

Generated result JSON files are test output, not product configuration. Obsolete natural-language fixtures must not be reintroduced.
