#!/bin/bash
# SYS-003: /ping - Simple ping test

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="SYS-003"
TEST_NAME="/ping - Simple ping test"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"

# Execute
log_step "Sending /ping command"
RESPONSE=$(send_telegram_command "/ping")
stop_timer

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from bot"
fi

log_step "Response received (${RESPONSE_TIME}s)"
echo "$RESPONSE"

# Verify
log_step "Verifying response"

assert_contains "$RESPONSE" "pong" "Response contains 'pong'" || exit_test "$TEST_ID" "FAIL" "Expected 'pong' in response"
assert_response_time 2 "Response time < 2s" || exit_test "$TEST_ID" "FAIL" "Response too slow"

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
