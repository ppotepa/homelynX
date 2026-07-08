#!/bin/bash
# Dual-path E2E test framework
# Tests both CLI and Telegram adapters

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"
source "$SCRIPT_DIR/cli.sh"
source "$SCRIPT_DIR/telegram.sh"
source "$SCRIPT_DIR/assertions.sh"

# Current adapter being tested
CURRENT_ADAPTER=""

# Run test for both adapters
run_dual() {
    local test_func="$1"
    local test_id="$2"
    
    if [ -z "$test_func" ]; then
        log_error "run_dual: test function name required"
        return 1
    fi
    
    # CLI path
    CURRENT_ADAPTER="cli"
    log_step "=== Testing via CLI adapter ==="
    if ! $test_func "cli"; then
        log_error "CLI path failed"
        [ -n "$test_id" ] && exit_test "$test_id" "FAIL" "CLI adapter test failed"
        return 1
    fi
    log_success "CLI path passed"
    
    # Telegram path (skip when test endpoint unavailable — e.g. bot not running)
    if ! check_test_endpoint; then
        log_warning "Skipping Telegram adapter — test endpoint unavailable at ${TEST_ENDPOINT}"
        log_success "CLI-only pass (Telegram skipped)"
        return 0
    fi

    CURRENT_ADAPTER="telegram"
    log_step "=== Testing via Telegram adapter ==="
    if ! $test_func "telegram"; then
        log_error "Telegram path failed"
        [ -n "$test_id" ] && exit_test "$test_id" "FAIL" "Telegram adapter test failed"
        return 1
    fi
    log_success "Telegram path passed"
    
    log_success "Both adapters passed"
    return 0
}

# Adapter-agnostic command execution
bot_call() {
    local target="$1"
    shift
    
    case "$CURRENT_ADAPTER" in
        cli)
            cli_call "$target" "$@"
            ;;
        telegram)
            send_telegram_command "$target"
            ;;
        *)
            log_error "bot_call: CURRENT_ADAPTER not set or unknown: $CURRENT_ADAPTER"
            return 1
            ;;
    esac
}

# Extract comparable data from CLI JSON response
extract_cli_field() {
    local json="$1"
    local path="$2"
    echo "$json" | jq -r "$path" 2>/dev/null
}

# Extract comparable data from Telegram text response
extract_tg_field() {
    local text="$1"
    local pattern="$2"
    echo "$text" | grep -oP "$pattern" | head -1
}

# Compare results between CLI and Telegram
assert_parity() {
    local cli_val="$1"
    local tg_val="$2"
    local desc="$3"
    
    if [ "$cli_val" = "$tg_val" ]; then
        log_assertion "PASS" "Parity check: $desc (both: $cli_val)"
        return 0
    else
        log_assertion "FAIL" "Parity mismatch: $desc"
        log_error "  CLI:      $cli_val"
        log_error "  Telegram: $tg_val"
        return 1
    fi
}

# Get current adapter name
get_adapter() {
    echo "$CURRENT_ADAPTER"
}

# Check if running CLI adapter
is_cli() {
    [ "$CURRENT_ADAPTER" = "cli" ]
}

# Check if running Telegram adapter
is_telegram() {
    [ "$CURRENT_ADAPTER" = "telegram" ]
}

export -f run_dual bot_call extract_cli_field extract_tg_field assert_parity
export -f get_adapter is_cli is_telegram
