#!/bin/bash
# SYS-001: Help Command — Dual Path Test (CLI + Telegram)
# Tests /help command via both adapters

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../../helpers/dual-path.sh"

TEST_ID="SYS-001"
TEST_NAME="Help Command (Dual Path)"

log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Test function — runs for each adapter
test_help() {
    local adapter="$1"
    
    if is_cli; then
        # CLI path — list capabilities
        RESULT=$(cli_list --json)
        
        assert_json_valid "$RESULT" "Capabilities list is valid JSON" || return 1
        
        CAP_COUNT=$(echo "$RESULT" | jq '.capabilities | length')
        assert_greater_than "$CAP_COUNT" "0" "Has at least one capability" || return 1
        
        log_info "Found $CAP_COUNT capabilities"
        
    elif is_telegram; then
        # Telegram path — send /help command
        RESULT=$(send_telegram_command "/help" 30)
        
        assert_not_empty "$RESULT" "Received response from bot" || return 1
        assert_contains "$RESULT" "help" "Response contains help info" || return 1
        
        log_info "Help response: ${RESULT:0:100}..."
    fi
    
    return 0
}

# Run dual-path test
run_dual test_help "$TEST_ID"
TEST_RESULT=$?

stop_timer

if [ $TEST_RESULT -eq 0 ]; then
    exit_test "$TEST_ID" "PASS" "Help command passed on all adapters (${TEST_DURATION}s)"
else
    exit_test "$TEST_ID" "FAIL" "Help command failed on one or more adapters"
fi
