#!/bin/bash
# NL-001: Natural Language przez CLI

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/cli.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="NL-001"
TEST_NAME="Natural Language przez CLI - 'pokaż capabilities'"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Execute
log_step "Wywołanie agent run z tekstem 'pokaż capabilities'"
START_TIME=$(date +%s.%N)
RESPONSE=$(cli_agent_run "pokaż capabilities")
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
assert_json_contains "$RESPONSE" "capabilities" "Odpowiedź zawiera 'capabilities'" || exit_test "$TEST_ID" "FAIL" "Brak 'capabilities'"
assert_response_time 10 "Czas odpowiedzi < 10s" || exit_test "$TEST_ID" "FAIL" "Zbyt wolna odpowiedź"

# Report
exit_test "$TEST_ID" "PASS" "Wszystkie asercje przeszły"
