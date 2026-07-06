# shellcheck shell=bash

configure_jackett() {
  local jackett_config_path
  local server_config
  local api_key

  jackett_config_path="$(absolute_path "$(get_env_value JACKETT_CONFIG_PATH)")"
  [ "$jackett_config_path" != "$PROJECT_DIR/" ] || jackett_config_path="$PROJECT_DIR/jackett-config"

  for _ in $(seq 1 60); do
    server_config="$(find "$jackett_config_path" -name ServerConfig.json -print -quit 2>/dev/null || true)"
    if [ -n "$server_config" ]; then
      api_key="$(sed -n 's/.*"APIKey"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$server_config" | head -n 1)"
      if [ -n "$api_key" ]; then
        set_env_value JACKETT_API_KEY "$api_key"
        log "Configured Jackett API key from $server_config."
        return
      fi
    fi
    sleep 2
  done

  fail "Could not read Jackett API key automatically from $jackett_config_path."
}
open_jackett_ui_session() {
  local cookie_file="$1"

  curl -sSL -c "$cookie_file" -b "$cookie_file" -o /dev/null \
    --connect-timeout 3 \
    --max-time 15 \
    "http://127.0.0.1:9117/api/v2.0/indexers"
}
configure_jackett_flaresolverr() {
  local cookie_file
  local server_config_file
  local response_file
  local status

  cookie_file="$(mktemp)"
  server_config_file="$(mktemp)"
  response_file="$(mktemp)"

  open_jackett_ui_session "$cookie_file"

  status="$(curl -sS -o "$server_config_file" -w '%{http_code}' -b "$cookie_file" \
    --connect-timeout 3 \
    --max-time 15 \
    "http://127.0.0.1:9117/api/v2.0/server/config" || true)"

  if [ "$status" != "200" ]; then
    rm -f "$cookie_file" "$server_config_file" "$response_file"
    fail "Could not read Jackett server config through the local UI API."
  fi

  sed -i -E 's#"FlareSolverrUrl"[[:space:]]*:[[:space:]]*(null|"[^"]*")#"FlareSolverrUrl":"http://flaresolverr:8191"#' "$server_config_file"

  status="$(curl -sS -o "$response_file" -w '%{http_code}' -b "$cookie_file" \
    -H 'Content-Type: application/json' \
    --data-binary "@$server_config_file" \
    --connect-timeout 3 \
    --max-time 20 \
    "http://127.0.0.1:9117/api/v2.0/server/config" || true)"

  rm -f "$cookie_file" "$server_config_file" "$response_file"

  case "$status" in
    2*) log "Configured Jackett FlareSolverr URL." ;;
    *) fail "Could not configure Jackett FlareSolverr URL." ;;
  esac
}
configure_jackett_indexers() {
  local preset
  local cookie_file
  local indexers_file
  local config_file
  local response_file
  local indexer
  local is_configured
  local status
  local configured=""
  local already_configured=""
  local skipped=""
  local failed=""

  preset="$(get_env_value JACKETT_INDEXERS_PRESET)"
  if [ -z "$preset" ]; then
    warn "JACKETT_INDEXERS_PRESET is empty; no Jackett indexers will be auto-configured."
    return
  fi

  cookie_file="$(mktemp)"
  indexers_file="$(mktemp)"
  config_file="$(mktemp)"
  response_file="$(mktemp)"

  open_jackett_ui_session "$cookie_file"

  status="$(curl -sS -o "$indexers_file" -w '%{http_code}' -b "$cookie_file" \
    --connect-timeout 3 \
    --max-time 20 \
    "http://127.0.0.1:9117/api/v2.0/indexers" || true)"

  if [ "$status" != "200" ]; then
    rm -f "$cookie_file" "$indexers_file" "$config_file" "$response_file"
    fail "Could not read Jackett indexer list through the local UI API."
  fi

  for indexer in $(printf '%s' "$preset" | tr ',' ' '); do
    is_configured="$(python3 - "$indexers_file" "$indexer" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as f:
    indexers = json.load(f)

target = sys.argv[2]
for indexer in indexers:
    if indexer.get("id") == target:
        print("yes" if indexer.get("configured") else "no")
        break
else:
    print("missing")
PY
)"

    if [ "$is_configured" = "yes" ]; then
      already_configured="${already_configured}${indexer} "
      continue
    fi

    if [ "$is_configured" = "missing" ]; then
      skipped="${skipped}${indexer} "
      continue
    fi

    status="$(curl -sS -o "$config_file" -w '%{http_code}' -b "$cookie_file" \
      --connect-timeout 3 \
      --max-time 20 \
      "http://127.0.0.1:9117/api/v2.0/indexers/${indexer}/config" || true)"

    if [ "$status" = "404" ]; then
      skipped="${skipped}${indexer} "
      continue
    fi

    if [ "$status" != "200" ]; then
      failed="${failed}${indexer} "
      continue
    fi

    if grep -q '"result"[[:space:]]*:[[:space:]]*"error"' "$config_file"; then
      failed="${failed}${indexer} "
      continue
    fi

    status="$(curl -sS -o "$response_file" -w '%{http_code}' -b "$cookie_file" \
      -H 'Content-Type: application/json' \
      --data-binary "@$config_file" \
      --connect-timeout 3 \
      --max-time 30 \
      "http://127.0.0.1:9117/api/v2.0/indexers/${indexer}/config" || true)"

    case "$status" in
      2*)
        configured="${configured}${indexer} "
        python3 - "$indexers_file" "$indexer" <<'PY'
import json
import sys

path, target = sys.argv[1], sys.argv[2]
with open(path, encoding="utf-8") as f:
    indexers = json.load(f)

for indexer in indexers:
    if indexer.get("id") == target:
        indexer["configured"] = True
        break

with open(path, "w", encoding="utf-8") as f:
    json.dump(indexers, f)
PY
        ;;
      *) failed="${failed}${indexer} " ;;
    esac
  done

  rm -f "$cookie_file" "$indexers_file" "$config_file" "$response_file"

  if [ -n "$already_configured" ]; then
    log "Jackett public indexers already configured: $already_configured"
  fi

  if [ -n "$configured" ]; then
    log "Configured missing Jackett public indexers: $configured"
  fi

  if [ -n "$skipped" ]; then
    warn "These preset Jackett indexers are not available in this Jackett build and were skipped: $skipped"
  fi

  if [ -n "$failed" ]; then
    warn "These Jackett indexers could not be configured automatically: $failed"
  fi

  if [ -z "$configured" ] && [ -z "$already_configured" ]; then
    fail "No Jackett indexers are configured. Open http://localhost:9117 and add indexers manually."
  fi
}
