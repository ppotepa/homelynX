#!/bin/bash
# SYS-002: /health - Check engine health

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="SYS-002"
TEST_NAME="/health - Check engine health"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"

# Execute
log_step "Sending /health command"
RESPONSE=$(send_telegram_command "/health")
stop_timer

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from bot"
fi

log_step "Response received (${RESPONSE_TIME}s)"
echo "$RESPONSE"

# Verify
log_step "Verifying response"

assert_contains "$RESPONSE" "healthy" "Response indicates healthy status" || exit_test "$TEST_ID" "FAIL" "Health status not found"
assert_not_contains "$RESPONSE" "error" "Response does not contain errors" || exit_test "$TEST_ID" "FAIL" "Error found in response"
assert_response_time 2 "Response time < 2s" || exit_test "$TEST_ID" "FAIL" "Response too slow"

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
