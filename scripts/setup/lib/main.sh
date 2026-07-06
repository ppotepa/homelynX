# shellcheck shell=bash

main() {
  parse_args "$@"

  if [ "$(uname -s)" != "Linux" ]; then
    fail "This setup script is intended for Linux."
  fi

  ensure_sudo_context

  log "Project directory: $PROJECT_DIR"
  if is_truthy "$REINSTALL"; then
    log "Running in reinstall mode. Existing data directories are preserved."
  fi
  ensure_tools
  ensure_env_file
  write_default_env
  ensure_telegram_tokens
  recreate_containers_for_reinstall
  configure_portal_auth
  configure_portal_port
  configure_media_library
  configure_zerotier
  configure_surveillance_devices
  ensure_local_files
  configure_tts_audio_override
  start_backend_services
  configure_jackett
  configure_jackett_flaresolverr
  configure_jackett_indexers
  configure_qbittorrent
  configure_qbittorrent_download_paths
  configure_qbittorrent_categories
  start_bot
  print_summary
}
