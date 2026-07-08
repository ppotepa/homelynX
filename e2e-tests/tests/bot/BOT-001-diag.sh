#!/bin/bash
# BOT-001: /diag - Run diagnostics

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="BOT-001"
TEST_NAME="/diag - Run diagnostics"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"

# Execute
log_step "Sending /diag command"
RESPONSE=$(send_telegram_command "/diag")
stop_timer

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from bot"
fi

log_step "Response received (${RESPONSE_TIME}s)"
echo "$RESPONSE"

# Verify
log_step "Verifying response"

assert_not_empty "$RESPONSE" "Response is not empty" || exit_test "$TEST_ID" "FAIL" "Empty response"
assert_response_time 5 "Response time < 5s" || exit_test "$TEST_ID" "FAIL" "Response too slow"

# Response should contain diagnostic information
assert_contains "$RESPONSE" "diag" "Response mentions diagnostics" || \
assert_contains "$RESPONSE" "status" "Response mentions status" || \
assert_contains "$RESPONSE" "ok" "Response contains OK status" || \
assert_contains "$RESPONSE" "OK" "Response contains OK status" || \
exit_test "$TEST_ID" "FAIL" "Response doesn't contain diagnostic info"

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
