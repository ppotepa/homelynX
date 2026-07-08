# shellcheck shell=bash

get_env_value() {
  local key="$1"
  if [ ! -f "$ENV_FILE" ]; then
    return 0
  fi

  grep -E "^${key}=" "$ENV_FILE" | tail -n 1 | cut -d '=' -f 2- || true
}
set_env_value() {
  local key="$1"
  local value="$2"
  local escaped

  escaped="$(printf '%s' "$value" | sed -e 's/[\/&|]/\\&/g')"

  if grep -qE "^${key}=" "$ENV_FILE"; then
    sed -i "s|^${key}=.*|${key}=${escaped}|" "$ENV_FILE"
  else
    printf '%s=%s\n' "$key" "$value" >> "$ENV_FILE"
  fi
}
set_env_default() {
  local key="$1"
  local value="$2"
  local current

  current="$(get_env_value "$key")"
  if is_truthy "$REINSTALL" || is_placeholder "$current"; then
    set_env_value "$key" "$value"
  fi
}
ensure_env_file() {
  if [ ! -f "$ENV_FILE" ]; then
    [ -f "$ENV_EXAMPLE" ] || fail "Missing .env.example"
    cp "$ENV_EXAMPLE" "$ENV_FILE"
    log "Created .env from .env.example."
  else
    log ".env already exists; keeping existing values where possible."
  fi
}
prompt_secret_env_value() {
  local key="$1"
  local prompt="$2"
  local required="${3:-false}"
  local value

  value="$(get_env_value "$key")"
  if ! is_truthy "$REINSTALL" && ! is_placeholder "$value"; then
    log "$key is already configured."
    return
  fi

  if [ ! -t 0 ]; then
    if is_truthy "$required" && is_placeholder "$value"; then
      fail "$key is required. Set it in .env and rerun setup."
    fi
    if is_placeholder "$value"; then
      warn "$key is not configured. Set it in .env later if you want this bot enabled."
    else
      log "$key is already configured; keeping it because setup is running without an interactive terminal."
    fi
    return
  fi

  value="$(read_secret_masked "$prompt")" || fail "Input interrupted."

  if [ -z "$value" ]; then
    if is_truthy "$required"; then
      fail "$key cannot be empty."
    fi
    warn "$key was left empty. This bot will stay disabled until you set it in .env."
    return
  fi

  set_env_value "$key" "$value"
}
ensure_telegram_tokens() {
  local main_token
  local surv_token
  local coord_token

  prompt_secret_env_value TELEGRAM_BOT_TOKEN "Main Telegram bot token: " true


  main_token="$(get_env_value TELEGRAM_BOT_TOKEN)"

}
write_default_env() {
  set_env_value TELEGRAM_ALLOWED_USERS ""
  set_env_value TELEGRAM_ALLOWED_USERS_FILE "allowed-users.cfg"
  set_env_default TELEGRAM_BOOTSTRAP_FIRST_USER "true"
  set_env_value TELEGRAM_ADMIN_CHAT_ID ""
  set_env_value TELEGRAM_NOTIFICATION_CHAT_ID ""


  set_env_value QBIT_HOST "qbittorrent"
  set_env_value QBIT_PORT "8080"
  set_env_value QBIT_USERNAME "admin"
  set_env_default QBIT_PASSWORD "$(random_password)"
  set_env_value QBIT_HTTPS "false"

  set_env_value JACKETT_HOST "jackett"
  set_env_value JACKETT_PORT "9117"
  set_env_value JACKETT_HTTPS "false"
  set_env_value JACKETT_INDEXERS_PRESET "1337x,thepiratebay,limetorrents,torrentdownloads,torrentdownload,eztv,yts,nyaasi,therarbg,kickasstorrents-to,extratorrent-st,kickasstorrents-ws,knaben,magnetcat,torrentcore,uindex,internetarchive,torrentproject2"
  set_env_value JACKETT_SEARCH_INDEXERS "1337x,thepiratebay,limetorrents,eztv,yts,nyaasi,therarbg,knaben,internetarchive"
  set_env_value JACKETT_DISABLED_INDEXERS "magnetz,torrentgalaxyclone"
  set_env_value JACKETT_TIMEOUT_SECONDS "15"

  set_env_default MEDIA_LIBRARY_PATH "/home/${SUDO_USER:-${USER:-ppotepa}}/mediaserver"
  set_env_default HOST_DOWNLOAD_PATH "$(get_env_value MEDIA_LIBRARY_PATH)/downloads"
  set_env_default COMPLETED_HOST_PATH "$(get_env_value MEDIA_LIBRARY_PATH)/downloads/completed"
  set_env_default MEDIA_ORGANIZER_SOURCE "$(get_env_value MEDIA_LIBRARY_PATH)/downloads/completed"
  set_env_value DOWNLOAD_PATH "/downloads"
  set_env_value COMPLETED_PATH "/downloads/completed"
  set_env_value TEMP_PATH "/downloads/incomplete"
  set_env_value DOWNLOAD_MOVIES_PATH "/downloads/completed/movies"
  set_env_value DOWNLOAD_TV_PATH "/downloads/completed/shows"
  set_env_value DOWNLOAD_SHOWS_PATH "/downloads/completed/shows"
  set_env_value DOWNLOAD_MUSIC_PATH "/downloads/completed/music"
  set_env_value DOWNLOAD_GAMES_PATH "/downloads/completed/games"
  set_env_value DOWNLOAD_SOFTWARE_PATH "/downloads/completed/software"
  set_env_value DOWNLOAD_BOOKS_PATH "/downloads/completed/books"
  set_env_value DOWNLOAD_ANIME_PATH "/downloads/completed/anime"
  set_env_value DOWNLOAD_OTHER_PATH "/downloads/completed/other"
  set_env_default TELEGRAM_MAX_UPLOAD_MB "50"
  set_env_default MEDIA_DOWNLOAD_TIMEOUT_SECONDS "1200"
  set_env_default YTDLP_COOKIES_FILE "/app/cookies/facebook.txt"
  set_env_default PORTAL_ENABLED "true"
  set_env_default PORTAL_PORT "80"
  set_env_default PORTAL_AUTH_ENABLED "true"
  set_env_default PORTAL_USERNAME "admin"
  set_env_default PORTAL_PASSWORD_HASH ""
  set_env_default PORTAL_SESSION_SECRET "$(random_password)"
  set_env_default LLM_AUDIT_TOKEN "$(random_password)"
  set_env_default LLM_AUDIT_RETENTION_DAYS "30"
  set_env_default LLM_AUDIT_URL "http://127.0.0.1/api/llm/audit"
  set_env_default PORTAL_PUBLIC_URL "http://homelynx.zt/llm"
  set_env_default PORTAL_LOCAL_URL "http://localhost/llm"
  set_env_default PORTAL_ADMIN_URL "http://homelynx.zt/admin"
  set_env_default BOT_NATURAL_LANGUAGE_ENABLED "true"
  set_env_default BOT_NATURAL_LANGUAGE_TIMEOUT_SECONDS "45"
  set_env_default BOT_NATURAL_LANGUAGE_PLANNER_NUM_PREDICT "192"
  set_env_default BOT_NATURAL_LANGUAGE_RESPONDER_NUM_PREDICT "192"
  set_env_default BOT_NATURAL_LANGUAGE_USE_FALLBACKS "true"
  set_env_default BOT_NATURAL_LANGUAGE_KEEP_ALIVE "-1"
  set_env_default BOT_NATURAL_LANGUAGE_FAST_ROUTER_ENABLED "true"
  set_env_default BOT_NATURAL_LANGUAGE_OUTPUT_FORMAT "schema"
  set_env_default BOT_NATURAL_LANGUAGE_DETERMINISTIC_FIRST "false"
  set_env_default BOT_NATURAL_LANGUAGE_DETERMINISTIC_RESPONSES "true"
  set_env_default HOMELYNX_LLM_SYSTEM_PROMPT_PROFILE "compact"
  set_env_default BOT_QUERY_MAX_ITERATIONS "3"
  set_env_default BOT_QUERY_LLM_CRITIC_ENABLED "true"
  set_env_default BOT_QUERY_HUMANIZER_ENABLED "true"
  set_env_default BOT_QUERY_TIMEOUT_SECONDS "10"
  set_env_default E2E_BENCHMARK_MODE "live"
  set_env_default BOT_NATURAL_LANGUAGE_MIN_CONFIDENCE "0.45"
  set_env_default JELLYFIN_ENABLED "true"
  set_env_default JELLYFIN_PORT "8096"
  set_env_default JELLYFIN_CONFIG_PATH "./jellyfin-config"
  set_env_default MEDIA_ORGANIZER_MODE "hardlink"
  set_env_default MEDIA_ORGANIZER_MIN_CONFIDENCE "0.70"
  set_env_default MEDIA_ORGANIZER_LLM_ENABLED "true"
  set_env_default MEDIA_ORGANIZER_LLM_MODEL "qwen3:0.6b"
  set_env_default MEDIA_ORGANIZER_LLM_URL "http://127.0.0.1:11434"
  set_env_default LLM_AUDIT_URL "http://127.0.0.1/api/llm/audit"

  set_env_value SEARCH_LIMIT "50"
  set_env_value MIN_SEEDERS "1"
  set_env_value SEARCH_TIMEOUT "30"

  set_env_default DEBUG "false"
  set_env_default COMPOSE_PROJECT_NAME "homelynx"
  set_env_default LOG_LEVEL "INFO"
  set_env_default LOG_FILE "/app/logs/homelynx.log"
  set_env_default LOG_MAX_BYTES "10485760"
  set_env_default LOG_BACKUP_COUNT "5"
  set_env_value PLUGIN_DIR "plugins/hot"
  set_env_default TTS_HOST "tts"
  set_env_default TTS_PORT "5055"
  set_env_default TTS_HTTPS "false"
  set_env_default TTS_DEFAULT_LANGUAGE "auto"
  set_env_default TTS_PLAYBACK_ENABLED "true"
  set_env_default TTS_PLAYBACK_BACKEND "auto"
  set_env_default TTS_PLAYBACK_DEVICE "JBL Go 4"
  set_env_default TTS_PULSE_SINK ""
  set_env_default TTS_PIPEWIRE_TARGET ""
  set_env_default TTS_MAX_TEXT_CHARS "1000"
  set_env_default TTS_PL_VOICE_NAME "pl_PL-meski_wg_glos-medium"
  set_env_default TTS_PL_MODEL_URL "https://huggingface.co/WitoldG/polish_piper_models/resolve/main/pl_PL-meski_wg_glos-medium.onnx"
  set_env_default TTS_PL_CONFIG_URL "https://huggingface.co/WitoldG/polish_piper_models/resolve/main/pl_PL-meski_wg_glos-medium.onnx.json"

  set_env_default LLM_ENABLED "true"
  set_env_default LLM_HOST "llm"
  set_env_default LLM_PORT "11434"
  set_env_default LLM_MODEL "qwen3:0.6b"
  set_env_default LLM_PLANNER_MODEL "qwen2.5:1.5b"
  set_env_default LLM_RESPONDER_MODEL "gemma3:1b"
  set_env_default LLM_EXECUTOR_MODEL "gemma3:1b"
  set_env_default LLM_TIMEOUT_SECONDS "20"
  set_env_default LLM_MAX_TRANSCRIPT_CHARS "1600"
  set_env_default LLM_CPUS "8.0"
  set_env_default LLM_MEM_LIMIT "12g"
  set_env_default OLLAMA_KEEP_ALIVE "-1"
  set_env_default OLLAMA_CONTEXT_LENGTH "8192"
  set_env_default OLLAMA_NUM_PARALLEL "1"
  set_env_default OLLAMA_MAX_LOADED_MODELS "2"
  set_env_default OLLAMA_MAX_QUEUE "64"
  set_env_default OLLAMA_LOAD_TIMEOUT "10m"
  set_env_default BOT_NATURAL_LANGUAGE_KEEP_ALIVE "-1"
  set_env_default BOT_NATURAL_LANGUAGE_FAST_ROUTER_ENABLED "true"
  set_env_default BOT_NATURAL_LANGUAGE_OUTPUT_FORMAT "schema"
  set_env_default BOT_NATURAL_LANGUAGE_DETERMINISTIC_FIRST "false"
  set_env_default BOT_NATURAL_LANGUAGE_DETERMINISTIC_RESPONSES "true"
  set_env_default HOMELYNX_LLM_SYSTEM_PROMPT_PROFILE "compact"
  set_env_default BOT_QUERY_MAX_ITERATIONS "3"
  set_env_default BOT_QUERY_LLM_CRITIC_ENABLED "true"
  set_env_default BOT_QUERY_HUMANIZER_ENABLED "true"
  set_env_default BOT_QUERY_TIMEOUT_SECONDS "10"
  set_env_default ZEROTIER_ENABLED "false"
  set_env_default ZEROTIER_NETWORK_ID ""
  set_env_default ZEROTIER_INSTALL_IF_MISSING "true"
  set_env_default ZEROTIER_DNS_ENABLED "false"
  set_env_default ZEROTIER_DNS_DOMAIN "homelynx.zt"
  set_env_default ZEROTIER_DNS_SERVER_IP ""

  set_env_value PUID "$(id -u)"
  set_env_value PGID "$(id -g)"
  set_env_value TZ "${TZ:-Europe/Warsaw}"
}
