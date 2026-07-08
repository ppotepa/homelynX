#!/bin/bash
# SYS-002: Health Check — Dual Path Test (CLI + Telegram)
# Tests /health command via both adapters

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../../helpers/dual-path.sh"

TEST_ID="SYS-002"
TEST_NAME="Health Check (Dual Path)"

log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Test function — runs for each adapter
test_health() {
    local adapter="$1"
    
    if is_cli; then
        # CLI path — call system.health capability
        RESULT=$(cli_call "system.health" --json)
        
        assert_json_valid "$RESULT" "Health response is valid JSON" || return 1
        assert_json_field "$RESULT" ".RawResult.Success" "true" "Health check succeeded" || return 1
        
        # Extract health details
        STATUS=$(extract_cli_field "$RESULT" ".RawResult.CapabilityResult.Data.status")
        assert_equals "healthy" "$STATUS" "System status is healthy" || return 1
        
        log_info "Health status: $STATUS"
        
    elif is_telegram; then
        # Telegram path — send /health command
        RESULT=$(send_telegram_command "/health" 30)
        
        assert_not_empty "$RESULT" "Received response from bot" || return 1
        assert_contains "$RESULT" "healthy" "Response contains 'healthy'" || return 1
        
        log_info "Health response: ${RESULT:0:100}..."
    fi
    
    return 0
}

# Run dual-path test
run_dual test_health "$TEST_ID"
TEST_RESULT=$?

stop_timer

if [ $TEST_RESULT -eq 0 ]; then
    exit_test "$TEST_ID" "PASS" "Health check passed on all adapters (${TEST_DURATION}s)"
else
    exit_test "$TEST_ID" "FAIL" "Health check failed on one or more adapters"
fi
