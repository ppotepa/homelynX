# shellcheck shell=bash

log() {
  printf '\n[install] %s\n' "$1"
}
warn() {
  printf '\n[install] WARNING: %s\n' "$1" >&2
}
fail() {
  printf '\n[install] ERROR: %s\n' "$1" >&2
  exit 1
}
usage() {
  cat <<'USAGE_EOF'
Usage: ./install.sh [--reinstall]

Options:
  --reinstall  Reconfigure existing settings and recreate containers without deleting data directories.
  -h, --help   Show this help.

Requires: Linux, Docker, and sudo access (or run as root) for system-level steps.
USAGE_EOF
}
parse_args() {
  while [ "$#" -gt 0 ]; do
    case "$1" in
      --reinstall)
        REINSTALL=true
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        fail "Unknown option: $1"
        ;;
    esac
    shift
  done
}
require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Missing required command: $1"
}
is_root_user() {
  [ "$(id -u)" -eq 0 ]
}
can_use_sudo() {
  command -v sudo >/dev/null 2>&1
}
sudo_session_valid() {
  sudo -n true 2>/dev/null
}
ensure_sudo_context() {
  if is_root_user; then
    log "Privilege context: running as root."
    return 0
  fi

  if ! can_use_sudo; then
    fail "sudo is required for Homelynx install (ZeroTier, systemd, optional packages). Install sudo or run ./install.sh as root."
  fi

  if sudo_session_valid; then
    log "Privilege context: passwordless sudo available."
    return 0
  fi

  log "Privilege context: sudo authentication required for system steps."
  if ! sudo -v; then
    fail "Could not obtain sudo privileges. Run ./install.sh as a user in the sudo group."
  fi

  log "Privilege context: sudo session active."
}
compose() {
  docker compose "$@"
}
as_root() {
  if is_root_user; then
    "$@"
    return $?
  fi

  if ! can_use_sudo; then
    fail "Root privileges required but sudo is not installed."
  fi

  if ! sudo_session_valid; then
    if ! sudo -v; then
      fail "Sudo session expired or authentication failed. Re-run ./install.sh."
    fi
  fi

  sudo "$@"
}
is_truthy() {
  case "${1:-}" in
    1|true|TRUE|yes|YES|on|ON) return 0 ;;
  esac
  return 1
}
is_placeholder() {
  local value="${1:-}"
  [ -z "$value" ] && return 0

  case "$value" in
    your_*|change_me*|*telegram_bot_token*|*jackett_api_key*|your_secure_password_here)
      return 0
      ;;
  esac

  return 1
}
trim_value() {
  printf '%s' "$1" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'
}
redraw_secret_prompt() {
  local prompt="$1"
  local value="$2"
  local cursor="$3"
  local length="${#value}"
  local move_left=$(( length - cursor ))
  local stars

  stars="$(printf '%*s' "$length" '' | tr ' ' '*')"
  printf '\r\033[K%s%s' "$prompt" "$stars" >&2
  if [ "$move_left" -gt 0 ]; then
    printf '\033[%sD' "$move_left" >&2
  fi
}
read_secret_masked() {
  local prompt="$1"
  local value=""
  local char
  local cursor=0
  local length=0
  local seq
  local code
  local prefix
  local suffix

  printf '%s' "$prompt" >&2
  while IFS= read -rsn1 char; do
    case "$char" in
      "")
        break
        ;;
      $'\177'|$'\b')
        if [ "$cursor" -gt 0 ]; then
          prefix="${value:0:cursor-1}"
          suffix="${value:cursor}"
          value="${prefix}${suffix}"
          cursor=$(( cursor - 1 ))
          redraw_secret_prompt "$prompt" "$value" "$cursor"
        fi
        ;;
      $'\001')
        cursor=0
        redraw_secret_prompt "$prompt" "$value" "$cursor"
        ;;
      $'\003')
        printf '\n' >&2
        return 130
        ;;
      $'\005')
        cursor="${#value}"
        redraw_secret_prompt "$prompt" "$value" "$cursor"
        ;;
      $'\015')
        break
        ;;
      $'\025')
        value=""
        cursor=0
        redraw_secret_prompt "$prompt" "$value" "$cursor"
        ;;
      $'\033')
        IFS= read -rsn1 seq || continue
        case "$seq" in
          "[")
            IFS= read -rsn1 code || continue
            case "$code" in
              C)
                length="${#value}"
                if [ "$cursor" -lt "$length" ]; then
                  cursor=$(( cursor + 1 ))
                  redraw_secret_prompt "$prompt" "$value" "$cursor"
                fi
                ;;
              D)
                if [ "$cursor" -gt 0 ]; then
                  cursor=$(( cursor - 1 ))
                  redraw_secret_prompt "$prompt" "$value" "$cursor"
                fi
                ;;
              H|1|7)
                if [ "$code" = "1" ] || [ "$code" = "7" ]; then
                  IFS= read -rsn1 seq || true
                fi
                cursor=0
                redraw_secret_prompt "$prompt" "$value" "$cursor"
                ;;
              F|4|8)
                if [ "$code" = "4" ] || [ "$code" = "8" ]; then
                  IFS= read -rsn1 seq || true
                fi
                cursor="${#value}"
                redraw_secret_prompt "$prompt" "$value" "$cursor"
                ;;
              3)
                IFS= read -rsn1 seq || true
                if [ "$cursor" -lt "${#value}" ]; then
                  prefix="${value:0:cursor}"
                  suffix="${value:cursor+1}"
                  value="${prefix}${suffix}"
                  redraw_secret_prompt "$prompt" "$value" "$cursor"
                fi
                ;;
            esac
            ;;
          O)
            IFS= read -rsn1 code || continue
            case "$code" in
              H)
                cursor=0
                redraw_secret_prompt "$prompt" "$value" "$cursor"
                ;;
              F)
                cursor="${#value}"
                redraw_secret_prompt "$prompt" "$value" "$cursor"
                ;;
            esac
            ;;
        esac
        ;;
      *)
        prefix="${value:0:cursor}"
        suffix="${value:cursor}"
        value="${prefix}${char}${suffix}"
        cursor=$(( cursor + 1 ))
        redraw_secret_prompt "$prompt" "$value" "$cursor"
        ;;
    esac
  done
  printf '\n' >&2
  trim_value "$value"
  printf '\n'
}
absolute_path() {
  local path="$1"

  case "$path" in
    /*) printf '%s\n' "$path" ;;
    *) printf '%s/%s\n' "$PROJECT_DIR" "$path" ;;
  esac
}
random_password() {
  if command -v openssl >/dev/null 2>&1; then
    openssl rand -hex 18
  else
    od -An -N18 -tx1 /dev/urandom | tr -d ' \n'
  fi
}
hash_password() {
  python3 - "$1" <<'PY'
import hashlib
import os
import sys

password = sys.argv[1].encode()
salt = os.urandom(16)
iterations = 260000
digest = hashlib.pbkdf2_hmac("sha256", password, salt, iterations).hex()
print(f"pbkdf2_sha256:{iterations}:{salt.hex()}:{digest}")
PY
}
select_menu() {
  local prompt="$1"
  shift
  local options=("$@")
  local selected=0
  local key

  if [ "${#options[@]}" -eq 0 ]; then
    return 1
  fi

  if [ ! -t 0 ]; then
    printf '%s\n' "${options[0]}"
    return 0
  fi

  while true; do
    printf '\r\033[K%s\n' "$prompt" >&2
    for i in "${!options[@]}"; do
      if [ "$i" -eq "$selected" ]; then
        printf '  > %s\n' "${options[$i]}" >&2
      else
        printf '    %s\n' "${options[$i]}" >&2
      fi
    done

    IFS= read -rsn1 key
    if [[ "$key" == $'\x1b' ]]; then
      read -rsn2 key
      case "$key" in
        "[A") selected=$(( selected > 0 ? selected - 1 : ${#options[@]} - 1 )) ;;
        "[B") selected=$(( selected < ${#options[@]} - 1 ? selected + 1 : 0 )) ;;
      esac
    elif [[ "$key" == "" ]]; then
      printf '%s\n' "${options[$selected]}"
      return 0
    fi

    printf '\033[%sA' "$((${#options[@]} + 1))" >&2
  done
}
