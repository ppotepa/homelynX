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

## Practical tools

The bot also exposes deterministic utility commands:

```text
/chiptune notes="C4/8 E4/8 G4/4" bpm=140 chip=gb format=mp3
/chiptune notes="C4/8 E4/8 G4/4" chip=gbc       # MP3 is the default
/chiptune degrees="1/8 3/8 5/8 8/4" key=D scale=minor chip=nes
/chiptune generate=riff key=E scale=phrygian chip=sms bars=4 seed=42
/chiptune generate=song key=D scale=minor progression="i VI III VII" chip=genesis wave=fm
/chiptune generate=bassline key=E scale=minor chip=c64_8580 wave=saw
/chiptune format=mp3             # attach a .mid file
/chiptune inspect                # attach a .mid file and inspect programs, polyphony and arrangement loss
/read https://example.com/article
/screenshot https://example.com device=mobile format=png
/track add RR123456789PL label="Parcel" notify=important
/home set 52.2297 21.0122
/location save work 50.0614 19.9383
/distance home work
/map home
/verbosity medium       # plan status + periodic orchestrator heartbeat
/verbosity full         # live pipeline stages and heartbeat summary
```

Screenshots are delivered as Telegram photos when possible and automatically fall back to documents when Telegram rejects an oversized or very tall image. Web Reader and screenshots require Chromium (`/usr/bin/chromium` in Docker). Parcel polling uses AfterShip when `AFTERSHIP_API_KEY` is configured; without it, `/track` keeps a local record and manual lookup link. Location commands store only coordinates explicitly supplied by each user.

Chiptune rendering uses a pinned headless Furnace build in Docker for Game Boy, NES, SNES, Sega Master System, C64/SID 6581/8580, YM2612/Genesis, PC Engine, Atari TIA/POKEY, PC Speaker and ZX Beeper profiles. Composer buttons remain valid for seven days in the tools SQLite database.

Telegram progress verbosity defaults to `medium`. After `plan: submitting to orchestrator`, the existing status message is refreshed every 10 seconds with elapsed time while a long operation is running. Use `verbosity full` for live pipeline stages; use `verbosity off` to disable progress edits.

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
