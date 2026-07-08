#!/bin/bash
# DL-ERR-002: Select invalid index - Error handling

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="DL-ERR-002"
TEST_NAME="/select 999 - Error handling for invalid index"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"

# Setup: Start a search session
log_step "Starting search session"
SEARCH_RESPONSE=$(send_telegram_command "/download_search ubuntu")

if [[ -z "$SEARCH_RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from search command"
fi

log_step "Search session started"

# Execute: Try to select invalid index
log_step "Sending /select 999"
sleep 2
RESPONSE=$(send_telegram_command "/select 999")
stop_timer

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from bot"
fi

log_step "Response received (${RESPONSE_TIME}s)"
echo "$RESPONSE"

# Verify
log_step "Verifying error response"

assert_contains "$RESPONSE" "error" "Response contains error message" || \
assert_contains "$RESPONSE" "Error" "Response contains Error message" || \
assert_contains "$RESPONSE" "invalid" "Response mentions invalid" || \
assert_contains "$RESPONSE" "Invalid" "Response mentions Invalid" || \
assert_contains "$RESPONSE" "not found" "Response mentions not found" || \
exit_test "$TEST_ID" "FAIL" "No error indication in response"

assert_response_time 3 "Response time < 3s" || exit_test "$TEST_ID" "FAIL" "Response too slow"

# Cleanup
log_step "Cleaning up"
send_telegram_command "/cancel_search" > /dev/null 2>&1

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
