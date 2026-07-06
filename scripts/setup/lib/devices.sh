# shellcheck shell=bash

configure_surveillance_devices() {
  local current_camera
  local current_audio
  local camera_options=()
  local audio_options=()
  local selected_camera
  local selected_audio
  local item

  current_camera="$(get_env_value SURV_CAMERA_DEVICE)"
  current_audio="$(get_env_value SURV_AUDIO_DEVICE)"

  if [ -d /dev/v4l/by-id ]; then
    while IFS= read -r item; do
      camera_options+=("$item")
    done < <(find /dev/v4l/by-id -maxdepth 1 -type l -name '*video-index0' | sort | awk '
      /046d|Logitech|C920/ { print; next }
      { other[++n]=$0 }
      END { for (i=1; i<=n; i++) print other[i] }
    ')
  fi

  if [ "${#camera_options[@]}" -eq 0 ]; then
    while IFS= read -r item; do
      camera_options+=("$item")
    done < <(find /dev -maxdepth 1 -name 'video*' | sort)
  fi

  if [ "${#camera_options[@]}" -gt 0 ]; then
    if [ -t 0 ] || is_placeholder "$current_camera" || [ ! -e "$current_camera" ]; then
      selected_camera="$(select_menu "Select surveillance camera:" "${camera_options[@]}")"
      set_env_value SURV_CAMERA_DEVICE "$selected_camera"
      log "Selected surveillance camera: $selected_camera"
    else
      log "Surveillance camera already configured: $current_camera"
    fi
  else
    warn "No video devices found for surveillance."
  fi

  if command -v arecord >/dev/null 2>&1; then
    while IFS= read -r item; do
      audio_options+=("$item")
    done < <(arecord -L 2>/dev/null | awk '
      /^sysdefault:CARD=/ || /^front:CARD=/ {
        value=$0
        getline desc
        label=value " # " desc
        if (value ~ /C920/ || desc ~ /C920|Logitech|Webcam/) print value
        else other[++n]=value
      }
      END {
        for (i=1; i<=n; i++) print other[i]
        print "default"
      }
    ')
  fi

  if [ "${#audio_options[@]}" -gt 0 ]; then
    if [ -t 0 ] || is_placeholder "$current_audio" || [ "$current_audio" = "default" ]; then
      selected_audio="$(select_menu "Select surveillance microphone:" "${audio_options[@]}")"
      set_env_value SURV_AUDIO_DEVICE "$selected_audio"
      log "Selected surveillance microphone: $selected_audio"
    else
      log "Surveillance microphone already configured: $current_audio"
    fi
  else
    warn "No ALSA capture devices found for surveillance."
  fi
}
