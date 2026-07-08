#!/bin/bash
# TOR-001: /search - Search for torrents

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="TOR-001"
TEST_NAME="/search - Search for torrents"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"

# Execute
log_step "Sending /search ubuntu command"
RESPONSE=$(send_telegram_command "/search ubuntu")
stop_timer

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from bot"
fi

log_step "Response received (${RESPONSE_TIME}s)"
echo "$RESPONSE" | head -30

# Verify
log_step "Verifying response"

assert_not_empty "$RESPONSE" "Response is not empty" || exit_test "$TEST_ID" "FAIL" "Empty response"
assert_response_time 5 "Response time < 5s" || exit_test "$TEST_ID" "FAIL" "Response too slow"

# Response should contain search results or indicate no results
assert_contains "$RESPONSE" "ubuntu" "Response contains search term" || \
assert_contains "$RESPONSE" "result" "Response mentions results" || \
assert_contains "$RESPONSE" "found" "Response mentions found items" || \
assert_contains "$RESPONSE" "no " "Response indicates no results" || \
exit_test "$TEST_ID" "FAIL" "Response doesn't contain search results"

# Cleanup
log_step "Cleaning up"
send_telegram_command "/cancel_search" > /dev/null 2>&1

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
