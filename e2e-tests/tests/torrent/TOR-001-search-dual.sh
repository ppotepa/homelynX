#!/bin/bash
# TOR-001: Torrent Search — Dual Path Test (CLI + Telegram via userbot)
# Tests torrent search via both adapters

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../../helpers/dual-path.sh"

TEST_ID="TOR-001"
TEST_NAME="Torrent Search (Dual Path)"

log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Test function — runs for each adapter
test_search() {
    local adapter="$1"
    
    if is_cli; then
        # CLI path — call download.search capability
        RESULT=$(cli_call "download.search" --param query=ubuntu --json)
        
        assert_json_valid "$RESULT" "Search response is valid JSON" || return 1
        assert_json_field "$RESULT" ".RawResult.Success" "true" "Search succeeded" || return 1
        
        # Extract result count
        COUNT=$(extract_cli_field "$RESULT" ".RawResult.CapabilityResult.Data.count")
        assert_greater_than "$COUNT" "0" "Found at least one torrent" || return 1
        
        log_info "Found $COUNT torrents"
        
    elif is_telegram; then
        # Telegram path — send /search command via test endpoint
        RESULT=$(send_telegram_command "/search ubuntu")
        
        assert_not_empty "$RESULT" "Received response from bot" || return 1
        
        # Response should contain torrent info or "no results"
        log_info "Search response: ${RESULT:0:150}..."
    fi
    
    return 0
}

# Run dual-path test
run_dual test_search "$TEST_ID"
TEST_RESULT=$?

stop_timer

if [ $TEST_RESULT -eq 0 ]; then
    exit_test "$TEST_ID" "PASS" "Torrent search passed on all adapters (${TEST_DURATION}s)"
else
    exit_test "$TEST_ID" "FAIL" "Torrent search failed on one or more adapters"
fi
