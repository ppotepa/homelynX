#!/bin/bash
# DL-ERR-001: Invalid URL - Error handling

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="DL-ERR-001"
TEST_NAME="/download url=invalid - Error handling for invalid URL"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"

# Execute
log_step "Sending download command with invalid URL"
RESPONSE=$(send_telegram_command "/download url=not-a-valid-url")
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
exit_test "$TEST_ID" "FAIL" "No error indication in response"

assert_not_contains "$RESPONSE" "started" "Response does not indicate download started" || exit_test "$TEST_ID" "FAIL" "Download should not start with invalid URL"

assert_response_time 3 "Response time < 3s" || exit_test "$TEST_ID" "FAIL" "Response too slow"

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
