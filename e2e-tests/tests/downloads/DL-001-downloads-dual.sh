#!/bin/bash
# DL-001: Downloads List — Dual Path Test (CLI + Telegram)
# Tests /downloads command via both adapters

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../../helpers/dual-path.sh"

TEST_ID="DL-001"
TEST_NAME="Downloads List (Dual Path)"

log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Test function — runs for each adapter
test_downloads() {
    local adapter="$1"
    
    if is_cli; then
        # CLI path — call download.list capability
        RESULT=$(cli_call "download.list" --json)
        
        assert_json_valid "$RESULT" "Downloads response is valid JSON" || return 1
        assert_json_field "$RESULT" ".RawResult.Success" "true" "Download list succeeded" || return 1
        
        # Extract download count
        COUNT=$(extract_cli_field "$RESULT" ".RawResult.CapabilityResult.Data.downloads | length")
        log_info "Found $COUNT downloads"
        
    elif is_telegram; then
        # Telegram path — send /downloads command
        RESULT=$(send_telegram_command "/downloads" 30)
        
        assert_not_empty "$RESULT" "Received response from bot" || return 1
        
        # Response should mention downloads or be empty list
        log_info "Downloads response: ${RESULT:0:100}..."
    fi
    
    return 0
}

# Run dual-path test
run_dual test_downloads "$TEST_ID"
TEST_RESULT=$?

stop_timer

if [ $TEST_RESULT -eq 0 ]; then
    exit_test "$TEST_ID" "PASS" "Downloads list passed on all adapters (${TEST_DURATION}s)"
else
    exit_test "$TEST_ID" "FAIL" "Downloads list failed on one or more adapters"
fi
