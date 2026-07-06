# shellcheck shell=bash

ensure_local_files() {
  local host_download_path
  local media_library_path
  local qbit_config_path
  local jackett_config_path
  local jellyfin_config_path
  local owner_uid
  local owner_gid

  media_library_path="$(absolute_path "$(get_env_value MEDIA_LIBRARY_PATH)")"
  host_download_path="$(absolute_path "$(get_env_value HOST_DOWNLOAD_PATH)")"
  qbit_config_path="$(absolute_path "$(get_env_value QBIT_CONFIG_PATH)")"
  jackett_config_path="$(absolute_path "$(get_env_value JACKETT_CONFIG_PATH)")"
  jellyfin_config_path="$(absolute_path "$(get_env_value JELLYFIN_CONFIG_PATH)")"
  owner_uid="${SUDO_UID:-$(stat -c '%u' "$PROJECT_DIR")}"
  owner_gid="${SUDO_GID:-$(stat -c '%g' "$PROJECT_DIR")}"

  [ "$media_library_path" != "$PROJECT_DIR/" ] || media_library_path="/home/${SUDO_USER:-${USER:-ppotepa}}/mediaserver"
  [ "$host_download_path" != "$PROJECT_DIR/" ] || host_download_path="$media_library_path/downloads"
  [ "$qbit_config_path" != "$PROJECT_DIR/" ] || qbit_config_path="$PROJECT_DIR/qbittorrent-config"
  [ "$jackett_config_path" != "$PROJECT_DIR/" ] || jackett_config_path="$PROJECT_DIR/jackett-config"
  [ "$jellyfin_config_path" != "$PROJECT_DIR/" ] || jellyfin_config_path="$PROJECT_DIR/jellyfin-config"

  mkdir -p \
    "$PROJECT_DIR/cookies" \
    "$PROJECT_DIR/plugins/hot" \
    "$PROJECT_DIR/logs" \
    "$PROJECT_DIR/tts-data/models" \
    "$PROJECT_DIR/tts-data/output" \
    "$PROJECT_DIR/surveillance-data/segments" \
    "$PROJECT_DIR/surveillance-data/events" \
    "$PROJECT_DIR/surveillance-data/snapshots" \
    "$PROJECT_DIR/surveillance-data/clips" \
    "$PROJECT_DIR/coord-data" \
    "$PROJECT_DIR/llm-data" \
    "$PROJECT_DIR/portal-data" \
    "$media_library_path/movies" \
    "$media_library_path/shows" \
    "$media_library_path/music" \
    "$media_library_path/books" \
    "$media_library_path/anime" \
    "$media_library_path/software" \
    "$media_library_path/games" \
    "$media_library_path/other" \
    "$media_library_path/_organizer/plans" \
    "$media_library_path/_organizer/logs" \
    "$PROJECT_DIR/services/portal" \
    "$host_download_path" \
    "$host_download_path/completed" \
    "$host_download_path/completed/movies" \
    "$host_download_path/completed/shows" \
    "$host_download_path/completed/music" \
    "$host_download_path/completed/books" \
    "$host_download_path/completed/anime" \
    "$host_download_path/completed/software" \
    "$host_download_path/completed/games" \
    "$host_download_path/completed/other" \
    "$host_download_path/incomplete" \
    "$qbit_config_path" \
    "$jackett_config_path" \
    "$jellyfin_config_path"

  chown -R "$owner_uid:$owner_gid" \
    "$PROJECT_DIR/tts-data" \
    "$PROJECT_DIR/surveillance-data" \
    "$PROJECT_DIR/coord-data" \
    "$PROJECT_DIR/llm-data" \
    "$PROJECT_DIR/portal-data" \
    "$jellyfin_config_path" \
    "$media_library_path" \
    "$host_download_path"

  if [ ! -f "$ALLOWED_USERS_FILE" ]; then
    cat > "$ALLOWED_USERS_FILE" <<'CFG_EOF'
# Telegram users allowed to use this bot.
# Format:
#   8153696940 ALL
#   @telegramUsername SAY
#
# Supported permissions:
#   ALL - all bot commands
#   SAY - speech/TTS commands only
#
# A bare numeric ID is treated as "ID ALL" for backward compatibility.
#
# The first Telegram user who sends a command to the bot is added automatically
# when this file has no allowed users.
CFG_EOF
    log "Created allowed-users.cfg."
  fi
}
