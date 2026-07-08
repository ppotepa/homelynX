#!/bin/bash
# CLI helper functions for E2E tests

# Load environment variables from .env
if [[ -z "$TELEGRAM_BOT_TOKEN" ]]; then
    set -a
    source "$(dirname "${BASH_SOURCE[0]}")/../config.env"
    set +a
fi

# Load project .env if it exists (export all variables)
PROJECT_ENV="$(dirname "${BASH_SOURCE[0]}")/../../.env"
if [[ -f "$PROJECT_ENV" ]]; then
    set -a
    source "$PROJECT_ENV"
    set +a
fi

# For CLI running on host, convert Docker service names to localhost
if [[ -z "$QBITTORRENT_URL" && -n "$QBIT_HOST" ]]; then
    QBIT_PORT="${QBIT_PORT:-8080}"
    QBIT_SCHEME="http"
    [[ "$QBIT_HTTPS" == "true" ]] && QBIT_SCHEME="https"
    export QBITTORRENT_URL="$QBIT_SCHEME://localhost:$QBIT_PORT"
fi
if [[ -z "$JACKETT_URL" && -n "$JACKETT_HOST" ]]; then
    JACKETT_PORT="${JACKETT_PORT:-9117}"
    JACKETT_SCHEME="http"
    [[ "$JACKETT_HTTPS" == "true" ]] && JACKETT_SCHEME="https"
    export JACKETT_URL="$JACKETT_SCHEME://localhost:$JACKETT_PORT"
fi
if [[ -z "$LLM_URL" && -n "$LLM_HOST" ]]; then
    LLM_PORT="${LLM_PORT:-11434}"
    LLM_SCHEME="http"
    [[ "$LLM_HTTPS" == "true" ]] && LLM_SCHEME="https"
    export LLM_URL="$LLM_SCHEME://localhost:$LLM_PORT"
fi
# Map Docker-style env vars to CLI-style env vars
[[ -n "$QBIT_USERNAME" ]] && export QBITTORRENT_USER="${QBITTORRENT_USER:-$QBIT_USERNAME}"
[[ -n "$QBIT_PASSWORD" ]] && export QBITTORRENT_PASS="${QBITTORRENT_PASS:-$QBIT_PASSWORD}"

# Ścieżka do CLI
CLI_PATH="/home/ppotepa/git/torrent-bot2/src/TorrentBot.Adapters.Cli/bin/Debug/net8.0/TorrentBot.Adapters.Cli"

# Wywołaj capability przez CLI
cli_call() {
    local capability="$1"
    shift

    log_step "Calling capability: $capability" >&2

    local -a cmd=("$CLI_PATH" "capability" "call" "$capability")
    local confirm_token=""

    # Parsuj argumenty
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --confirm=*)
                confirm_token="${1#--confirm=}"
                shift
                ;;
            --confirm)
                confirm_token="$2"
                shift 2
                ;;
            --json)
                shift
                ;;
            --param)
                cmd+=("--param" "$2")
                shift 2
                ;;
            --param=*)
                cmd+=("--param" "${1#--param=}")
                shift
                ;;
            *)
                shift
                ;;
        esac
    done

    # Dodaj confirm token jeśli podany
    if [[ -n "$confirm_token" ]]; then
        cmd+=("--confirm" "$confirm_token")
    fi

    # Dodaj JSON output dla łatwiejszego parsowania
    cmd+=("--json")

    START_TIME=$(date +%s.%N)
    RESPONSE=$("${cmd[@]}" 2>/dev/null)
    END_TIME=$(date +%s.%N)
    RESPONSE_TIME=$(echo "$END_TIME - $START_TIME" | bc 2>/dev/null || echo "0")

    if [[ $? -ne 0 ]]; then
        log_error "CLI call failed" >&2
        echo ""
        return 1
    fi

    echo "$RESPONSE"
    return 0
}

# Wykonaj natural language request przez CLI
cli_agent_run() {
    local text="$1"

    log_step "Running agent: $text" >&2

    START_TIME=$(date +%s.%N)
    RESPONSE=$($CLI_PATH agent run "$text" --json 2>/dev/null)
    END_TIME=$(date +%s.%N)
    RESPONSE_TIME=$(echo "$END_TIME - $START_TIME" | bc 2>/dev/null || echo "0")

    if [[ $? -ne 0 ]]; then
        log_error "Agent run failed" >&2
        echo ""
        return 1
    fi

    echo "$RESPONSE"
    return 0
}

# Wykonaj query przez CLI
cli_query() {
    local source="$1"
    local where="${2:-}"

    log_step "Querying source: $source" >&2

    local cmd="$CLI_PATH query $source --json"

    if [[ -n "$where" ]]; then
        cmd="$cmd --where $where"
    fi

    START_TIME=$(date +%s.%N)
    RESPONSE=$(eval "$cmd" 2>/dev/null)
    END_TIME=$(date +%s.%N)
    RESPONSE_TIME=$(echo "$END_TIME - $START_TIME" | bc 2>/dev/null || echo "0")

    if [[ $? -ne 0 ]]; then
        log_error "Query failed" >&2
        echo ""
        return 1
    fi

    echo "$RESPONSE"
    return 0
}

# Lista capabilities przez CLI
cli_list() {
    log_step "Listing capabilities" >&2

    START_TIME=$(date +%s.%N)
    RESPONSE=$($CLI_PATH capabilities list --json 2>/dev/null)
    END_TIME=$(date +%s.%N)
    RESPONSE_TIME=$(echo "$END_TIME - $START_TIME" | bc 2>/dev/null || echo "0")

    if [[ $? -ne 0 ]]; then
        log_error "List failed" >&2
        echo ""
        return 1
    fi

    echo "$RESPONSE"
    return 0
}

# Export functions
export -f cli_call cli_agent_run cli_query cli_list
