#!/bin/bash
# MED-001: /media - List media files

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="MED-001"
TEST_NAME="/media - List media files"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"

# Execute
log_step "Sending /media command"
RESPONSE=$(send_telegram_command "/media")
stop_timer

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from bot"
fi

log_step "Response received (${RESPONSE_TIME}s)"
echo "$RESPONSE" | head -30

# Verify
log_step "Verifying response"

assert_not_empty "$RESPONSE" "Response is not empty" || exit_test "$TEST_ID" "FAIL" "Empty response"
assert_response_time 3 "Response time < 3s" || exit_test "$TEST_ID" "FAIL" "Response too slow"

# Response should mention media or indicate no files
assert_contains "$RESPONSE" "media" "Response mentions media" || \
assert_contains "$RESPONSE" "Media" "Response mentions Media" || \
assert_contains "$RESPONSE" "file" "Response mentions files" || \
assert_contains "$RESPONSE" "no " "Response indicates no files" || \
exit_test "$TEST_ID" "FAIL" "Response doesn't mention media"

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
