#!/bin/bash
# SYS-001: Lista capabilities przez CLI

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/cli.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="SYS-001"
TEST_NAME="Lista capabilities przez CLI"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Execute
log_step "Wywołanie capabilities list"
START_TIME=$(date +%s.%N)
RESPONSE=$(cli_list)
END_TIME=$(date +%s.%N)
RESPONSE_TIME=$(echo "$END_TIME - $START_TIME" | bc 2>/dev/null || echo "0")

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "Brak odpowiedzi z CLI"
fi

log_step "Odpowiedź otrzymana (${RESPONSE_TIME}s)"
echo "$RESPONSE" | head -20

# Verify
log_step "Weryfikacja odpowiedzi"

# Sprawdź czy to JSON
assert_json_valid "$RESPONSE" "Odpowiedź to valid JSON" || exit_test "$TEST_ID" "FAIL" "Invalid JSON"

# Sprawdź czy zawiera capabilities
assert_json_count "$RESPONSE" ".capabilities" "50" "Lista zawiera capabilities" || exit_test "$TEST_ID" "FAIL" "Brak capabilities"

# Sprawdź czy zawiera kluczowe capabilities
assert_json_contains "$RESPONSE" "system.help" "Zawiera system.help" || exit_test "$TEST_ID" "FAIL" "Brak system.help"
assert_json_contains "$RESPONSE" "system.health" "Zawiera system.health" || exit_test "$TEST_ID" "FAIL" "Brak system.health"
assert_json_contains "$RESPONSE" "torrent.search" "Zawiera torrent.search" || exit_test "$TEST_ID" "FAIL" "Brak torrent.search"

assert_response_time 3 "Czas odpowiedzi < 3s" || exit_test "$TEST_ID" "FAIL" "Zbyt wolna odpowiedź"

# Report
exit_test "$TEST_ID" "PASS" "Wszystkie asercje przeszły"
