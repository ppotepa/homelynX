# shellcheck shell=bash

wait_for_http() {
  local name="$1"
  local url="$2"
  local attempts="${3:-60}"
  local status

  for _ in $(seq 1 "$attempts"); do
    status="$(curl -sS -o /dev/null -w '%{http_code}' --connect-timeout 2 --max-time 5 "$url" 2>/dev/null || true)"
    case "$status" in
      2*|3*|4*) return 0 ;;
    esac
    sleep 2
  done

  fail "$name did not become ready at $url"
}
ensure_tools() {
  require_command docker
  require_command curl
  require_command sed
  require_command grep

  docker compose version >/dev/null 2>&1 || fail "Docker Compose plugin is required. Install Docker Compose v2."
  if docker info >/dev/null 2>&1; then
    return 0
  fi

  if can_use_sudo && sudo -n docker info >/dev/null 2>&1; then
    fail "Docker works via sudo but not for user $(id -un). Run: sudo usermod -aG docker $(id -un), then log in again."
  fi

  fail "Docker is not running or not installed. Start the Docker daemon and rerun ./install.sh."
}
recreate_containers_for_reinstall() {
  if ! is_truthy "$REINSTALL"; then
    return
  fi

  log "Reinstall mode enabled: recreating Homelynx compose stack without deleting data directories."
  compose down --remove-orphans || warn "docker compose down returned a non-zero status; continuing with setup."
}
start_backend_services() {
  local jellyfin_enabled
  local jellyfin_port
  local portal_enabled
  local portal_port
  local services

  jellyfin_enabled="$(get_env_value JELLYFIN_ENABLED)"
  jellyfin_port="$(get_env_value JELLYFIN_PORT)"
  jellyfin_port="${jellyfin_port:-8096}"
  portal_enabled="$(get_env_value PORTAL_ENABLED)"
  portal_port="$(get_env_value PORTAL_PORT)"
  portal_port="${portal_port:-80}"

  log "Starting Homelynx portal, qBittorrent, Jackett, FlareSolverr, TTS, LLM, surveillance, coord-input and media library services."
  services=(qbittorrent jackett flaresolverr tts llm surveillance coord-input)
  if is_truthy "$portal_enabled"; then
    services+=(portal)
  fi
  if is_truthy "$jellyfin_enabled"; then
    services+=(jellyfin)
  fi
  compose up -d --build "${services[@]}"

  if is_truthy "$portal_enabled"; then
    wait_for_http "Homelynx Portal" "http://127.0.0.1:${portal_port}" 60
  fi
  wait_for_http "qBittorrent" "http://127.0.0.1:8080" 90
  wait_for_http "Jackett" "http://127.0.0.1:9117" 90
  if is_truthy "$jellyfin_enabled"; then
    wait_for_http "Jellyfin" "http://127.0.0.1:${jellyfin_port}" 180
  fi
  wait_for_http "TTS" "http://127.0.0.1:5055/health" 120
  wait_for_http "LLM" "http://127.0.0.1:11434/api/tags" 180
  ensure_llm_model
  wait_for_http "Surveillance" "http://127.0.0.1:5060/health" 120
  wait_for_http "Coord Input" "http://127.0.0.1:5070/health" 90
}
start_bot() {
  local services=(homelynx-bot)

  log "Building and starting Homelynx bot container: ${services[*]}."
  compose up -d --build "${services[@]}"
}
