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
  prompt_secret_env_value SURV_TELEGRAM_BOT_TOKEN "Surveillance Telegram bot token (Enter to skip): " false
  prompt_secret_env_value COORD_TELEGRAM_BOT_TOKEN "Coordinate Telegram bot token (Enter to skip): " false

  main_token="$(get_env_value TELEGRAM_BOT_TOKEN)"
  surv_token="$(get_env_value SURV_TELEGRAM_BOT_TOKEN)"
  coord_token="$(get_env_value COORD_TELEGRAM_BOT_TOKEN)"

  if [ -n "$surv_token" ] && [ "$surv_token" = "$main_token" ]; then
    warn "SURV_TELEGRAM_BOT_TOKEN matches TELEGRAM_BOT_TOKEN. Do not run two polling bots on the same Telegram token."
  fi
  if [ -n "$coord_token" ] && [ "$coord_token" = "$main_token" ]; then
    warn "COORD_TELEGRAM_BOT_TOKEN matches TELEGRAM_BOT_TOKEN. Do not run two polling bots on the same Telegram token."
  fi
  if [ -n "$surv_token" ] && [ -n "$coord_token" ] && [ "$surv_token" = "$coord_token" ]; then
    warn "SURV_TELEGRAM_BOT_TOKEN matches COORD_TELEGRAM_BOT_TOKEN. Do not run two polling bots on the same Telegram token."
  fi
}
write_default_env() {
  set_env_value TELEGRAM_ALLOWED_USERS ""
  set_env_value TELEGRAM_ALLOWED_USERS_FILE "allowed-users.cfg"
  set_env_default TELEGRAM_BOOTSTRAP_FIRST_USER "true"
  set_env_value TELEGRAM_ADMIN_CHAT_ID ""
  set_env_value TELEGRAM_NOTIFICATION_CHAT_ID ""
  set_env_default SURV_TELEGRAM_BOT_TOKEN ""
  set_env_default SURV_TELEGRAM_CHAT_ID ""
  set_env_default SURV_TELEGRAM_MIN_SEVERITY "medium"

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
  set_env_default SURV_HOST "surveillance"
  set_env_default SURV_PORT "5060"
  set_env_default SURV_CAMERA_DEVICE "/dev/v4l/by-id/usb-046d_HD_Pro_Webcam_C920-video-index0"
  set_env_default SURV_AUDIO_DEVICE "sysdefault:CARD=C920"
  set_env_default SURV_AUDIO_FILTER "highpass=f=120,lowpass=f=7000,afftdn=nf=-25,alimiter=limit=0.85"
  set_env_default SURV_SEGMENT_SECONDS "5"
  set_env_default SURV_RECORDER_WATCHDOG_SECONDS "20"
  set_env_default SURV_RECORDER_RESTART_DELAY_SECONDS "3"
  set_env_default SURV_SNAPSHOT_RAM_LIMIT_BYTES "1073741824"
  set_env_default SURV_RETENTION_HOURS "24"
  set_env_default SURV_EVENT_RETENTION_DAYS "14"
  set_env_default SURV_INCIDENT_GAP_SECONDS "90"
  set_env_default SURV_RECORD_VIDEO "true"
  set_env_default SURV_RECORD_AUDIO "true"
  set_env_default SURV_ANALYZER_WORKERS "2"
  set_env_default SURV_SPEECH_RATIO_THRESHOLD "0.18"
  set_env_default SURV_LOUD_PEAK_THRESHOLD "0.45"
  set_env_default SURV_EVENT_GAP_SECONDS "25"
  set_env_default SURV_NOTIFY_ENABLED "true"
  set_env_default SURV_TRANSCRIBE_ENABLED "true"
  set_env_default SURV_TRANSCRIBE_MODEL "small"
  set_env_default SURV_TRANSCRIBE_LANGUAGE "auto"
  set_env_default SURV_TRANSCRIBE_COMPUTE_TYPE "int8"
  set_env_default SURV_TRANSCRIBE_BEAM_SIZE "5"
  set_env_default SURV_TRANSCRIBE_VAD "true"
  set_env_default SURV_TRANSCRIBE_VAD_MIN_SILENCE_MS "500"
  set_env_default SURV_TRANSCRIBE_WORD_TIMESTAMPS "false"
  set_env_default SURV_TRANSCRIBE_CONDITION_ON_PREVIOUS_TEXT "false"
  set_env_default SURV_JOB_WORKERS "1"
  set_env_default SURV_JOB_MAX_ATTEMPTS "3"
  set_env_default SURV_JOB_POLL_SECONDS "1.0"
  set_env_default SURV_JOB_DONE_RETENTION_HOURS "48"
  set_env_default SURV_JOB_FAILED_RETENTION_HOURS "168"
  set_env_default SURV_PREVIEW_GIF_ENABLED "true"
  set_env_default SURV_PREVIEW_GIF_SPEED "5.0"
  set_env_default SURV_PREVIEW_GIF_FPS "6"
  set_env_default SURV_PREVIEW_GIF_WIDTH "480"
  set_env_default SURV_PREVIEW_GIF_AUDIO_HEIGHT "84"
  set_env_default SURV_PREVIEW_GIF_MAX_SOURCE_SECONDS "180"
  set_env_default SURV_OPERATOR_SUMMARY_DEFAULT_HOURS "24"
  set_env_default SURV_OPERATOR_SUMMARY_MAX_EVENTS "120"
  set_env_default SURV_OPERATOR_SUMMARY_MAX_TRANSCRIPT_CHARS "14000"
  set_env_default SURV_OPERATOR_SUMMARY_TIMEOUT_SECONDS "45"
  set_env_default SURV_NOTIFY_MIN_INTERVAL_SECONDS "45"
  set_env_default SURV_NOTIFY_BACKLOG_SUPPRESS_SECONDS "180"
  set_env_default SURV_RECENT_SEGMENT_BUFFER "240"
  set_env_default SURV_SPEECH_EXTENDED_SEGMENTS "2"
  set_env_default SURV_NOISE_SPIKE_MULTIPLIER "2.4"
  set_env_default SURV_DEVICE_ALERT_COOLDOWN_SECONDS "300"
  set_env_default SURV_MOTION_ENABLED "true"
  set_env_default SURV_FACE_ENABLED "true"
  set_env_default SURV_PERSON_ENABLED "true"
  set_env_default SURV_MOTION_DIFF_THRESHOLD "18"
  set_env_default SURV_MOTION_MIN_RATIO "0.015"
  set_env_default SURV_FACE_MIN_SIZE "64"
  set_env_default SURV_PERSON_HOG_STRIDE "8"
  set_env_default SURV_PERSON_MIN_CONFIDENCE "0.45"
  set_env_default SURV_EVENT_TRIGGER_KINDS "PERSON_DETECTION,FACE_DETECTION"
  set_env_default SURV_LLM_SUMMARIES_ENABLED "true"
  set_env_default SURV_LLM_MIN_SEVERITY "medium"
  set_env_default SURV_CPUS "2.0"
  set_env_default SURV_MEM_LIMIT "2g"
  set_env_default COORD_INPUT_API_KEY "$(random_password)"
  set_env_default COORD_TELEGRAM_BOT_TOKEN ""
  set_env_default COORD_TELEGRAM_CHAT_ID ""
  set_env_default COORD_TELEGRAM_ALLOWED_USERS ""
  set_env_default COORD_TELEGRAM_BOOTSTRAP_CHAT "true"
  set_env_default COORD_NOTIFY_ENABLED "false"
  set_env_default COORD_NOTIFY_MIN_INTERVAL_SECONDS "60"
  set_env_default COORD_NOTIFY_MIN_DISTANCE_METERS "25"
  set_env_default COORD_HISTORY_LIMIT "5000"
  set_env_default COORD_BOT_POLL_SECONDS "2"
  set_env_default COORD_LLM_SUMMARY_ENABLED "true"
  set_env_default COORD_LLM_MODEL "qwen3:0.6b"
  set_env_default COORD_LLM_TIMEOUT_SECONDS "20"
  set_env_default COORD_ANDROID_ARCHIVE_ENABLED "true"
  set_env_default COORD_ANDROID_DIRECT_TELEGRAM "false"
  set_env_default COORD_ANDROID_HIGH_POWER_GPS "false"
  set_env_default COORD_START_TRACKING "true"
  set_env_default COORD_INTERVAL_SECONDS "60"
  set_env_default COORD_MIN_DISTANCE_METERS "25"
  set_env_default LLM_ENABLED "true"
  set_env_default LLM_HOST "llm"
  set_env_default LLM_PORT "11434"
  set_env_default LLM_MODEL "qwen3:0.6b"
  set_env_default LLM_PLANNER_MODEL ""
  set_env_default LLM_RESPONDER_MODEL ""
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
