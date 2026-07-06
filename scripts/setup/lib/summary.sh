# shellcheck shell=bash

print_summary() {
  local portal_port
  local portal_url

  portal_port="$(get_env_value PORTAL_PORT)"
  portal_port="${portal_port:-80}"
  if [ "$portal_port" = "80" ]; then
    portal_url="http://localhost"
  else
    portal_url="http://localhost:$portal_port"
  fi

  cat <<'SUMMARY_EOF'

[install] Setup finished.

Services:
  Media root:  MEDIA_LIBRARY_PATH from .env
SUMMARY_EOF
  cat <<SUMMARY_EOF
  Portal:      $portal_url
  Admin:       $portal_url/admin
  LLM audit:   $portal_url/llm
SUMMARY_EOF
  cat <<'SUMMARY_EOF'
  Telegram bot: Docker service homelynx-bot (C#)
  qBittorrent:  http://localhost:8080
  Jellyfin:     http://localhost:8096
  Jackett:      http://localhost:9117
  FlareSolverr: http://localhost:8191
  LLM/Ollama:   http://localhost:11434
  Surveillance: http://localhost:5060/health
  Coord Input:  http://localhost:5070/health

Access control:
  allowed-users.cfg was created.
  The first Telegram user who sends a command to the bot will be added automatically.
  Add more Telegram users to allowed-users.cfg, for example:
    8153696940 ALL
    @telegramUsername SAY

ZeroTier remote access:
  Set ZEROTIER_NETWORK_ID in .env and rerun setup to join a private ZeroTier network.
  After setup, authorize this host in my.zerotier.com.
  From your phone, install ZeroTier, join the same network, then open Jellyfin at http://<server-zerotier-ip>:8096.
  Optional private DNS:
    ZEROTIER_DNS_ENABLED=true
    ZEROTIER_DNS_DOMAIN=homelynx.zt
  Then set the ZeroTier Central DNS server to this host ZeroTier IP and search domain to homelynx.zt.
  Portal URL: http://homelynx.zt
  Example URLs: http://jellyfin.homelynx.zt:8096, http://qbit.homelynx.zt:8080, http://coords.homelynx.zt:5070.

Coord Input:
  Android app path: android/coord-input
  Server URL in Android app: http://<server-lan-ip>:5070/location
  API key in Android app: COORD_INPUT_API_KEY from .env
  Telegram bot token for @potepa_home_coord_input_bot: set COORD_TELEGRAM_BOT_TOKEN in .env.
  Default flow: Android archives to SQLite every COORD_INTERVAL_SECONDS; Telegram direct push is disabled.
  Bot commands: /last, /timeline 17m, /timeline 1h, /timeline 35d, /timeline 1y, /history, /status.
  Android Wi-Fi deploy: ./android_deploy.sh --adb-target PHONE_IP:ADB_PORT
  Optional pairing: ./android_deploy.sh --adb-pair PHONE_IP:PAIR_PORT --adb-pair-code CODE --adb-target PHONE_IP:ADB_PORT

Media organizer:
  Dry run: ./scripts/media_organize.sh --dry-run
  Apply:   ./scripts/media_organize.sh --apply
  Default mode is hardlink, so qBittorrent can keep seeding from downloads while Jellyfin sees organized media.

LLM audit:
  UI:      $portal_url/llm
  Stores:  ./portal-data/llm_audit.sqlite3
  Records local LLM calls from surveillance summaries, coordinate timeline summaries, and media organizer classification.

Useful commands:
  docker compose ps
  docker compose logs -f homelynx-bot
  docker compose logs -f coord-input
  docker compose up -d --build coord-input
  docker compose restart homelynx-bot
SUMMARY_EOF
}
