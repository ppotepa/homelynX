# Homelynx

Private-homelab **media automation** control plane (.NET 8 / C#), replacing the legacy Python Telegram bot.

Homelynx is the intelligent center for torrent search (Jackett) → download (qBittorrent) → media library (Jellyfin), with plugin-backed text commands and optional TTS. Interfaces:

- **Telegram** — production bot (`homelynx-bot` container)
- **CLI** — diagnostics, dry-run, automation (`TorrentBot.Adapters.Cli`)

## Install (full homelab stack)

```bash
./install.sh
```

Requires **sudo** (or root) for system steps (ZeroTier DNS, systemd). Docker must be running and accessible to your user.

The installer configures `.env`, starts satellite services (qBittorrent, Jackett, portal, TTS, Jellyfin), then builds and starts the **C# `homelynx-bot`** service.

Reinstall without wiping data:

```bash
./install.sh --reinstall
```

### Bot only (development)

```bash
cd src
dotnet run --project TorrentBot.Adapters.Telegram.Host -- --harness   # no Telegram token
dotnet run --project TorrentBot.Adapters.Cli -- capability call system.health --json
```

Production container:

```bash
docker compose up -d --build homelynx-bot
docker compose logs -f homelynx-bot
```

## Reinstall

```bash
./install.sh --reinstall
```

Recreates containers defined in `docker-compose.yaml` (same project `homelynx`) without wiping data volumes.

Satellite Docker services, ports, and `.env` keys (`QBIT_HOST`, `JACKETT_HOST`, …) are resolved automatically by the C# bootstrap.

## Project layout

```
homelynx/
├── install.sh              # Full stack installer
├── docker-compose.yaml     # Homelynx stack (project name: homelynx)
├── Dockerfile              # C# Telegram host image
├── acl/                    # ACL presets
├── services/               # Portal, TTS
├── src/                    # .NET solution (assemblies still named TorrentBot.*)
└── docs/
```

## Documentation

- [docs/README.md](docs/README.md)
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- [docs/FUNCTIONALITY.md](docs/FUNCTIONALITY.md)

## License

Private / internal homelab tool.
