#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="$PROJECT_DIR/.env"
ENV_EXAMPLE="$PROJECT_DIR/.env.example"
ALLOWED_USERS_FILE="$PROJECT_DIR/allowed-users.cfg"
REINSTALL=false

cd "$PROJECT_DIR"

SETUP_LIB_DIR="$PROJECT_DIR/scripts/setup/lib"
for module in common.sh env.sh services.sh zerotier.sh media.sh portal.sh devices.sh files.sh tts.sh llm.sh jackett.sh qbittorrent.sh summary.sh main.sh; do
  # shellcheck source=/dev/null
  source "$SETUP_LIB_DIR/$module"
done

main "$@"