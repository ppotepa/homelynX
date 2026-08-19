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

## Text tools

The bot also exposes small, explicit command plugins. They do not use an LLM and keep personal data isolated by Telegram user id:

```text
/note add text                 /note list | /note delete ID
/note edit ID text             /note tag ID tag1,tag2 | /note search text
/todo add text                 /todo list | /todo done ID | /todo undo ID
/todo edit ID text             /todo clear | /todo delete ID
/remind 20m text               /reminders
/remind cancel ID              /timer 10m text | /timer cancel ID
/timers                        /poll question | yes | no
/poll list | /poll close ID    /poll vote ID OPTION | /poll results ID
/paste add text                /paste list | /paste show ID
/calc (12.5 * 4) + 2           /convert 10 km mi
/password 32                   /passphrase 5
/hash sha512 text              /uuid | /base64 text | /base64 decode VALUE
/slug text                     /date Europe/Warsaw | /timestamp 1720000000
/choose red, blue, green       /dice 2d6
/weather Warsaw                /time Europe/Warsaw | /rate EUR PLN
/qr url https://example.com    /qr wifi ssid="Home" password="secret" security=WPA
/barcode code128 HOMELYNX-42   /barcode ean13 5901234123457
/shorten https://example.com slug=home expires=7d max=20
/shorten list|disable CODE    /url inspect|clean|redirects https://example.com
/json format|minify JSON       /json query $.path {"path":{"value":1}}
/urlencode text | /urlencode decode VALUE
/color #ff8800                 /text_stats multiline text
/base 255 dec hex              /mediainfo movie.mp4
/thumbnail movie.mp4 00:01     /extract_audio movie.mp4 192k
/gif movie.mp4 00:10 00:15     /compress movie.mp4 28
/chiptune notes="C4/8 E4/8 G4/4" bpm=140 preset=gameboy format=mp3
/chiptune (attach .mid file)  /read https://example.com/article
/screenshot https://example.com device=mobile format=png
/track add RR123456789PL label="Parcel" notify=important
/track list|refresh ID|pause ID|resume ID
/home set 52.2297 21.0122  /home show|delete
/location save work 52.23 21.01 | /location list
/distance home 50.0614 19.9383  /map home
/translate en pl text
/summarize text                /rewrite upper|lower|trim text
/extract_tasks text            /files recent|large|find|duplicates
/files move confirm relative/path | /trash list|restore ID
/services | /service logs       /service_logs filename
/webhook list|revoke ID        /webhook trigger ID
```

Notes, tasks, reminders, pastes, polls and webhook definitions are stored in SQLite. Docker stores this database at `/data/tools/homelynx-tools.db`; local development defaults to `./data/homelynx-tools.db`. DuckDB remains available for the existing query layer. LLM-dependent commands such as summarization, rewriting and semantic task extraction are intentionally not registered.

### Tool configuration

The QR and barcode commands generate files locally and Telegram delivers them as photo/document attachments. Short links use the same SQLite database and are served by the optional HTTP endpoint on port `8089`:

```bash
TORRENTBOT_SHORTENER_ENABLED=true
TORRENTBOT_SHORTENER_BASE_URL=https://go.example.com
TORRENTBOT_SHORTENER_BIND_URL=http://0.0.0.0:8089
```

Put HTTPS and authentication/rate limiting in front of the endpoint with a reverse proxy. The media tools accept only paths below `TORRENTBOT_MEDIA_ROOT`; FFmpeg/ffprobe must be installed. Generated Telegram attachments are capped at 45 MB, while larger results are retained under the media directory's `converted/` folder and return their path.

Web Reader and screenshots use a local Chromium executable (`/usr/bin/chromium` in Docker). `/track` uses AfterShip when `AFTERSHIP_API_KEY` is configured and otherwise keeps a local parcel record with a manual lookup link. `/home`, `/location`, `/distance` and `/map` store coordinates per Telegram user; they do not collect location automatically.

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
