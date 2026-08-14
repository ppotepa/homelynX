# Homelynx

Homelynx is a .NET 8 homelab media-control application with explicit Telegram and CLI commands.

Core flow:

`Telegram / CLI → command routing → capability registry → handler → integration → response`

There is no natural-language command planner. User actions are explicit commands such as `/search ubuntu`, `/select 2`, `/downloads`, `/status` and `/health`. Telegram buttons are supported for explicit selection and confirmation callbacks.

## Services

The Docker stack contains the Homelynx bot plus the services it integrates with: qBittorrent, Jackett, FlareSolverr, Jellyfin, portal and TTS.

## Install

```bash
./install.sh
```

Docker must be available. Some host setup performed by the installer can require elevated privileges.

Reinstall without intentionally wiping application data:

```bash
./install.sh --reinstall
```

### Development

```bash
cd src
dotnet run --project TorrentBot.Adapters.Telegram.Host -- --harness
dotnet run --project TorrentBot.Adapters.Cli -- capability call system.health --json
```

Production bot container:

```bash
docker compose up -d --build homelynx-bot
docker compose logs -f homelynx-bot
```

## Main capabilities

- torrent search, selection and torrent control
- download start/list/cancel
- jobs and download completion tracking
- media listing and TTS
- system status, health, metrics and capability help
- read-only structured queries through DuckDB
- ACL and confirmation checks for protected operations

Search state is intentionally retained between `/search` and `/select`; this is application session state, not conversational inference.

## Project layout

```text
homelynX/
├── install.sh
├── docker-compose.yaml
├── Dockerfile
├── acl/
├── services/
├── src/
└── docs/
```

See [docs/README.md](docs/README.md) for architecture and capability documentation.
