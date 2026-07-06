# shellcheck shell=bash

extract_qbit_temporary_password() {
  docker logs qbittorrent 2>&1 \
    | sed -n 's/.*temporary password is provided for this session:[[:space:]]*\([^[:space:]]*\).*/\1/p' \
    | tail -n 1
}
configure_qbittorrent() {
  local existing_password
  local temp_password
  local new_password
  local cookie_file
  local login_body_file
  local login_status
  local login_body
  local preferences_json

  existing_password="$(get_env_value QBIT_PASSWORD)"
  if ! is_placeholder "$existing_password"; then
    cookie_file="$(mktemp)"
    login_body_file="$(mktemp)"

    login_status="$(curl -sS -o "$login_body_file" -w '%{http_code}' -c "$cookie_file" \
      --data-urlencode "username=$(get_env_value QBIT_USERNAME)" \
      --data-urlencode "password=$existing_password" \
      "http://127.0.0.1:8080/api/v2/auth/login" || true)"
    login_body="$(cat "$login_body_file")"

    if [ "$login_status" = "200" ] || [ "$login_status" = "204" ] || printf '%s' "$login_body" | grep -q 'Ok'; then
      if ! is_truthy "$REINSTALL"; then
        rm -f "$cookie_file" "$login_body_file"
        log "qBittorrent credentials from .env are valid."
        return
      fi

      new_password="$(random_password)"
      preferences_json="{\"web_ui_username\":\"admin\",\"web_ui_password\":\"$new_password\"}"
      curl -fsS -b "$cookie_file" \
        --data-urlencode "json=$preferences_json" \
        "http://127.0.0.1:8080/api/v2/app/setPreferences" >/dev/null

      rm -f "$cookie_file" "$login_body_file"
      set_env_value QBIT_USERNAME "admin"
      set_env_value QBIT_PASSWORD "$new_password"
      log "Regenerated qBittorrent Web UI credentials for user admin."
      return
    fi

    rm -f "$cookie_file" "$login_body_file"
    warn "qBittorrent password in .env is not valid. Trying to repair credentials from qBittorrent temporary password."
  fi

  temp_password="$(extract_qbit_temporary_password || true)"
  if [ -z "$temp_password" ]; then
    fail "Could not find qBittorrent temporary password in container logs. Remove or move qbittorrent-config and run setup again."
  fi

  new_password="$(random_password)"
  cookie_file="$(mktemp)"
  login_body_file="$(mktemp)"

  login_status="$(curl -sS -o "$login_body_file" -w '%{http_code}' -c "$cookie_file" \
    --data-urlencode "username=admin" \
    --data-urlencode "password=$temp_password" \
    "http://127.0.0.1:8080/api/v2/auth/login" || true)"
  login_body="$(cat "$login_body_file")"

  if [ "$login_status" != "200" ] && [ "$login_status" != "204" ] && ! printf '%s' "$login_body" | grep -q 'Ok'; then
    rm -f "$cookie_file" "$login_body_file"
    fail "Could not log in to qBittorrent with its temporary password."
  fi

  preferences_json="{\"web_ui_username\":\"admin\",\"web_ui_password\":\"$new_password\"}"
  curl -fsS -b "$cookie_file" \
    --data-urlencode "json=$preferences_json" \
    "http://127.0.0.1:8080/api/v2/app/setPreferences" >/dev/null

  rm -f "$cookie_file" "$login_body_file"
  set_env_value QBIT_USERNAME "admin"
  set_env_value QBIT_PASSWORD "$new_password"
  log "Configured qBittorrent Web UI credentials for user admin."
}
configure_qbittorrent_download_paths() {
  local cookie_file
  local login_body_file
  local login_status
  local login_body
  local preferences_json

  cookie_file="$(mktemp)"
  login_body_file="$(mktemp)"
  login_status="$(curl -sS -o "$login_body_file" -w '%{http_code}' -c "$cookie_file" \
    --data-urlencode "username=$(get_env_value QBIT_USERNAME)" \
    --data-urlencode "password=$(get_env_value QBIT_PASSWORD)" \
    "http://127.0.0.1:8080/api/v2/auth/login" || true)"
  login_body="$(cat "$login_body_file")"

  if [ "$login_status" != "200" ] && [ "$login_status" != "204" ] && ! printf '%s' "$login_body" | grep -q 'Ok'; then
    rm -f "$cookie_file" "$login_body_file"
    warn "Could not log in to qBittorrent to configure download paths."
    return
  fi

  preferences_json='{"save_path":"/downloads/completed","temp_path_enabled":true,"temp_path":"/downloads/incomplete"}'
  curl -fsS -b "$cookie_file" \
    --data-urlencode "json=$preferences_json" \
    "http://127.0.0.1:8080/api/v2/app/setPreferences" >/dev/null || warn "Could not configure qBittorrent download paths."

  rm -f "$cookie_file" "$login_body_file"
  log "Configured qBittorrent default download paths."
}
configure_qbittorrent_categories() {
  local cookie_file
  local login_body_file
  local login_status
  local login_body
  local category
  local save_path

  cookie_file="$(mktemp)"
  login_body_file="$(mktemp)"
  login_status="$(curl -sS -o "$login_body_file" -w '%{http_code}' -c "$cookie_file" \
    --data-urlencode "username=$(get_env_value QBIT_USERNAME)" \
    --data-urlencode "password=$(get_env_value QBIT_PASSWORD)" \
    "http://127.0.0.1:8080/api/v2/auth/login" || true)"
  login_body="$(cat "$login_body_file")"

  if [ "$login_status" != "200" ] && [ "$login_status" != "204" ] && ! printf '%s' "$login_body" | grep -q 'Ok'; then
    rm -f "$cookie_file" "$login_body_file"
    warn "Could not log in to qBittorrent to configure media categories."
    return
  fi

  while IFS='|' read -r category save_path; do
    [ -n "$category" ] || continue
    curl -sS -b "$cookie_file" \
      --data-urlencode "category=$category" \
      --data-urlencode "savePath=$save_path" \
      "http://127.0.0.1:8080/api/v2/torrents/createCategory" >/dev/null || true
    curl -sS -b "$cookie_file" \
      --data-urlencode "category=$category" \
      --data-urlencode "savePath=$save_path" \
      "http://127.0.0.1:8080/api/v2/torrents/editCategory" >/dev/null || true
  done <<'CAT_EOF'
movies|/downloads/completed/movies
tv|/downloads/completed/shows
shows|/downloads/completed/shows
music|/downloads/completed/music
books|/downloads/completed/books
anime|/downloads/completed/anime
software|/downloads/completed/software
games|/downloads/completed/games
other|/downloads/completed/other
CAT_EOF

  rm -f "$cookie_file" "$login_body_file"
  log "Configured qBittorrent media categories."
}
