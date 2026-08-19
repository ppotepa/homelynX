# shellcheck shell=bash

print_summary() {
  cat <<'SUMMARY_EOF'

[install] Setup finished.

Services:
  Media root:  MEDIA_LIBRARY_PATH from .env
SUMMARY_EOF
  cat <<SUMMARY_EOF
  Telegram bot: Docker service homelynx-bot (C#)
  qBittorrent:  http://localhost:8080
  Jellyfin:     http://localhost:8096
  Jackett:      http://localhost:9117
  FlareSolverr: http://localhost:8191


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

Media organizer:
  Dry run: ./scripts/media_organize.sh --dry-run
  Apply:   ./scripts/media_organize.sh --apply
  Default mode is hardlink, so qBittorrent can keep seeding from downloads while Jellyfin sees organized media.

Useful commands:
  docker compose ps
  docker compose logs -f homelynx-bot
  docker compose restart homelynx-bot
SUMMARY_EOF
}
