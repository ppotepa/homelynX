# shellcheck shell=bash

configure_portal_auth() {
  local current_hash
  local password

  current_hash="$(get_env_value PORTAL_PASSWORD_HASH)"
  if ! is_truthy "$REINSTALL" && [ -n "$current_hash" ]; then
    log "Portal admin password is already configured."
    return
  fi

  require_command python3
  password="$(random_password)"
  set_env_value PORTAL_PASSWORD_HASH "$(hash_password "$password")"
  log "Generated portal admin credentials: username=$(get_env_value PORTAL_USERNAME), password=$password"
  log "Store this password. To reset it later, clear PORTAL_PASSWORD_HASH in .env and rerun setup."
}
port_is_available() {
  local port="$1"

  python3 - "$port" <<'PY'
import socket
import sys

port = int(sys.argv[1])
sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
try:
    sock.bind(("127.0.0.1", port))
except OSError:
    sys.exit(1)
finally:
    sock.close()
PY
}
configure_portal_port() {
  local portal_enabled
  local current_port
  local candidate
  local candidates=(8088 8085 8008 8081 8180 8888)

  portal_enabled="$(get_env_value PORTAL_ENABLED)"
  if ! is_truthy "$portal_enabled"; then
    return
  fi

  require_command python3
  current_port="$(get_env_value PORTAL_PORT)"
  current_port="${current_port:-80}"

  if port_is_available "$current_port"; then
    log "Portal port is available: $current_port"
    return
  fi

  warn "Portal port $current_port is already in use. Looking for a free fallback port."
  for candidate in "${candidates[@]}"; do
    if port_is_available "$candidate"; then
      set_env_value PORTAL_PORT "$candidate"
      warn "Using PORTAL_PORT=$candidate instead of $current_port."
      return
    fi
  done

  fail "No free portal port found. Set PORTAL_PORT in .env manually and rerun setup."
}
