# shellcheck shell=bash

configure_tts_audio_override() {
  local override_file="$PROJECT_DIR/docker-compose.override.yaml"
  local uid
  local gid
  local runtime_dir

  if [ ! -e /dev/snd ]; then
    warn "No /dev/snd device found. TTS will generate audio, but local speaker playback may be unavailable."
    return
  fi

  uid="${SUDO_UID:-$(stat -c '%u' "$PROJECT_DIR")}"
  gid="${SUDO_GID:-$(stat -c '%g' "$PROJECT_DIR")}"
  runtime_dir="/run/user/$uid"

  if [ ! -d "$runtime_dir" ]; then
    warn "$runtime_dir does not exist. TTS will use ALSA fallback instead of PipeWire/PulseAudio."
  fi

  cat > "$override_file" <<YAML
services:
  tts:
    user: "$uid:$gid"
    environment:
      XDG_RUNTIME_DIR: "$runtime_dir"
      DBUS_SESSION_BUS_ADDRESS: "unix:path=$runtime_dir/bus"
      PULSE_SERVER: "unix:$runtime_dir/pulse/native"
    devices:
      - "/dev/snd:/dev/snd"
      - "/dev/bus/usb:/dev/bus/usb"
    volumes:
      - "$runtime_dir:$runtime_dir"
    group_add:
      - audio
YAML

  log "Created docker-compose.override.yaml for TTS local audio playback."
}
