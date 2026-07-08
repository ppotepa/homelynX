#!/bin/bash
# DL-001: /downloads przez CLI

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/cli.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="DL-001"
TEST_NAME="/downloads przez CLI - lista pobrań"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Execute
log_step "Wywołanie download.list capability"
START_TIME=$(date +%s.%N)
RESPONSE=$(cli_call "download.list")
END_TIME=$(date +%s.%N)
RESPONSE_TIME=$(echo "$END_TIME - $START_TIME" | bc 2>/dev/null || echo "0")

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "Brak odpowiedzi z CLI"
fi

log_step "Odpowiedź otrzymana (${RESPONSE_TIME}s)"
echo "$RESPONSE" | head -30

# Verify
log_step "Weryfikacja odpowiedzi"

assert_json_valid "$RESPONSE" "Odpowiedź to valid JSON" || exit_test "$TEST_ID" "FAIL" "Invalid JSON"
assert_json_contains "$RESPONSE" "downloads" "Odpowiedź zawiera 'downloads'" || exit_test "$TEST_ID" "FAIL" "Brak 'downloads'"
assert_response_time 3 "Czas odpowiedzi < 3s" || exit_test "$TEST_ID" "FAIL" "Zbyt wolna odpowiedź"

# Report
exit_test "$TEST_ID" "PASS" "Wszystkie asercje przeszły"
