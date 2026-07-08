#!/bin/bash
# BOT-002: /plugins - Show plugin status

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="BOT-002"
TEST_NAME="/plugins - Show plugin status"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"

# Execute
log_step "Sending /plugins command"
RESPONSE=$(send_telegram_command "/plugins")
stop_timer

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from bot"
fi

log_step "Response received (${RESPONSE_TIME}s)"
echo "$RESPONSE"

# Verify
log_step "Verifying response"

assert_not_empty "$RESPONSE" "Response is not empty" || exit_test "$TEST_ID" "FAIL" "Empty response"
assert_response_time 3 "Response time < 3s" || exit_test "$TEST_ID" "FAIL" "Response too slow"

# Response should mention plugins
assert_contains "$RESPONSE" "plugin" "Response mentions plugins" || \
assert_contains "$RESPONSE" "Plugin" "Response mentions Plugins" || \
exit_test "$TEST_ID" "FAIL" "Response doesn't mention plugins"

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
