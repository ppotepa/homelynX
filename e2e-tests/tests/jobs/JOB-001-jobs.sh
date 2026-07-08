#!/bin/bash
# JOB-001: /jobs - List active jobs

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="JOB-001"
TEST_NAME="/jobs - List active jobs"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"

# Execute
log_step "Sending /jobs command"
RESPONSE=$(send_telegram_command "/jobs")
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

# Response should mention jobs or indicate no active jobs
assert_contains "$RESPONSE" "job" "Response mentions jobs" || \
assert_contains "$RESPONSE" "Job" "Response mentions Jobs" || \
assert_contains "$RESPONSE" "no active" "Response indicates no active jobs" || \
exit_test "$TEST_ID" "FAIL" "Response doesn't mention jobs"

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
