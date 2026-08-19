# Homelynx

Private-homelab **media automation** bot (.NET 8 / C#), replacing the legacy Python Telegram bot.

Homelynx handles torrent downloads (Jackett/qBittorrent) and public media URLs (yt-dlp/FFmpeg) → Jellyfin, with explicit plugin-backed text commands. Interfaces:

- **Telegram** — production bot (`homelynx-bot` container)
- **CLI** — diagnostics and automation (`TorrentBot.Adapters.Cli`)

## Install (full homelab stack)

```bash
./install.sh
```

Requires **sudo** (or root) for system steps (ZeroTier DNS, systemd). Docker must be running and accessible to your user.

The installer configures `.env`, starts qBittorrent, Jackett, FlareSolverr and Jellyfin, then builds and starts the **C# `homelynx-bot`** service.

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

## Media URLs

Paste a public YouTube, Facebook, Dailymotion, Vimeo, Instagram or TikTok URL, or use:

```text
/download_media <url> mp3 192
/download_media <url> mp4 720
/download_media <url> subtitles en pl
```

Media downloads are converted with yt-dlp and FFmpeg and stored in the Jellyfin music or movie library.
Subtitles are downloaded as SRT files without downloading the video and stored under the media root's `subtitles/Online` directory. Add `auto` to allow auto-generated captions.

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
├── services/               # Supporting service assets
├── src/                    # .NET solution (assemblies still named TorrentBot.*)
└── docs/
```

## Documentation

- [docs/README.md](docs/README.md)
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- [docs/FUNCTIONALITY.md](docs/FUNCTIONALITY.md)

## License

Private / internal homelab tool.
