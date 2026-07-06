# shellcheck shell=bash

configure_media_library() {
  local current
  local default_path
  local selected

  current="$(get_env_value MEDIA_LIBRARY_PATH)"
  default_path="/home/${SUDO_USER:-${USER:-ppotepa}}/mediaserver"
  selected="${current:-$default_path}"

  if [ -t 0 ]; then
    printf 'Media library folder [%s]: ' "$selected" >&2
    IFS= read -r selected_input || selected_input=""
    if [ -n "$selected_input" ]; then
      selected="$selected_input"
    fi
  fi

  selected="$(absolute_path "$selected")"
  set_env_value MEDIA_LIBRARY_PATH "$selected"
  set_env_value HOST_DOWNLOAD_PATH "$selected/downloads"
  set_env_value COMPLETED_HOST_PATH "$selected/downloads/completed"
  set_env_value MEDIA_ORGANIZER_SOURCE "$selected/downloads/completed"
  set_env_value DOWNLOAD_PATH "/downloads"
  set_env_value COMPLETED_PATH "/downloads/completed"
  set_env_value TEMP_PATH "/downloads/incomplete"
  set_env_default MEDIA_ORGANIZER_MODE "hardlink"
  set_env_default MEDIA_ORGANIZER_MIN_CONFIDENCE "0.70"
  set_env_default MEDIA_ORGANIZER_LLM_ENABLED "true"
  set_env_default MEDIA_ORGANIZER_LLM_MODEL "$(get_env_value LLM_MODEL)"
  set_env_default MEDIA_ORGANIZER_LLM_URL "http://127.0.0.1:11434"

  log "Media library path: $selected"
}
