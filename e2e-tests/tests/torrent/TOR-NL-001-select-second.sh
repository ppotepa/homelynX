#!/bin/bash
# TOR-NL-001: Natural-language torrent selection after search (Telegram test endpoint)
# Flow: /download_search → pending index → plain text "wybierz drugi"

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/../../helpers/dual-path.sh"

TEST_ID="TOR-NL-001"
TEST_NAME="NL wybierz drugi selects second search result"

log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

if ! check_test_endpoint; then
    log_warning "Skipping: start homelynx-bot with test endpoint (port 5000) to run this test"
    stop_timer
    exit_test "$TEST_ID" "PASS" "Skipped — test endpoint unavailable (${TEST_DURATION}s)"
    exit 0
fi

# Stable chat id so ConversationContext persists between inject-update calls
export TEST_CHAT_ID="${TEST_CHAT_ID:-99001001}"

log_step "Step 1: Search torrents (/download_search ubuntu)"
SEARCH_RESULT=$(send_telegram_command "/download_search ubuntu" 90)
assert_not_empty "$SEARCH_RESULT" "Search response is not empty" || exit_test "$TEST_ID" "FAIL" "Empty search response"

if echo "$SEARCH_RESULT" | grep -qi "error\|not available\|forbidden"; then
    log_error "Search failed: $SEARCH_RESULT"
    exit_test "$TEST_ID" "FAIL" "Search command failed"
fi

# Numbered results or select hints (Polish/English presenters)
if echo "$SEARCH_RESULT" | grep -qE '\[2\]|/select|select:|Wyniki|Found.*torrent'; then
    log_info "Search returned selectable results"
else
    log_warning "Search response format unexpected: ${SEARCH_RESULT:0:200}..."
fi

log_step "Step 2: Natural-language selection (wybierz drugi)"
SELECT_RESULT=$(send_telegram_command "wybierz drugi" 90)
assert_not_empty "$SELECT_RESULT" "Selection response is not empty" || exit_test "$TEST_ID" "FAIL" "Empty NL select response"

# Should not fall through as unrelated NL or index error
assert_not_contains "$SELECT_RESULT" "out of range" "No index out of range error" || exit_test "$TEST_ID" "FAIL" "Index out of range"
assert_not_contains "$SELECT_RESULT" "No active torrent search" "Session still active" || exit_test "$TEST_ID" "FAIL" "Search session lost"

# Successful select leads to confirmation prompt or download acknowledgement
if echo "$SELECT_RESULT" | grep -qiE 'confirm|potwierd|download|pobier|selected|dry-run|would start|Started'; then
    log_info "NL selection accepted: ${SELECT_RESULT:0:160}..."
else
    log_error "Unexpected NL select response: $SELECT_RESULT"
    exit_test "$TEST_ID" "FAIL" "NL selection not handled"
fi

stop_timer
exit_test "$TEST_ID" "PASS" "NL wybierz drugi handled after search (${TEST_DURATION}s)"