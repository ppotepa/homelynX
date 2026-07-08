#!/bin/bash
# SYS-001: /help - List all available commands

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="SYS-001"
TEST_NAME="/help - List all available commands"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"

# Execute
log_step "Sending /help command"
RESPONSE=$(send_telegram_command "/help")
stop_timer

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from bot"
fi

log_step "Response received (${RESPONSE_TIME}s)"
echo "$RESPONSE" | head -20

# Verify
log_step "Verifying response"

assert_contains "$RESPONSE" "BOT" "Response contains BOT section" || exit_test "$TEST_ID" "FAIL" "BOT section missing"
assert_contains "$RESPONSE" "DOWNLOAD" "Response contains DOWNLOAD section" || exit_test "$TEST_ID" "FAIL" "DOWNLOAD section missing"
assert_contains "$RESPONSE" "JOBS" "Response contains JOBS section" || exit_test "$TEST_ID" "FAIL" "JOBS section missing"
assert_contains "$RESPONSE" "MEDIA" "Response contains MEDIA section" || exit_test "$TEST_ID" "FAIL" "MEDIA section missing"
assert_contains "$RESPONSE" "SYSTEM" "Response contains SYSTEM section" || exit_test "$TEST_ID" "FAIL" "SYSTEM section missing"
assert_contains "$RESPONSE" "TORRENT" "Response contains TORRENT section" || exit_test "$TEST_ID" "FAIL" "TORRENT section missing"

assert_contains "$RESPONSE" "/help" "Response contains /help command" || exit_test "$TEST_ID" "FAIL" "/help command missing"
assert_contains "$RESPONSE" "/health" "Response contains /health command" || exit_test "$TEST_ID" "FAIL" "/health command missing"
assert_contains "$RESPONSE" "/downloads" "Response contains /downloads command" || exit_test "$TEST_ID" "FAIL" "/downloads command missing"

assert_response_time 3 "Response time < 3s" || exit_test "$TEST_ID" "FAIL" "Response too slow"

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
