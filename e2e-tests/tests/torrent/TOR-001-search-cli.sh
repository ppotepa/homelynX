#!/bin/bash
# TOR-001: /search przez CLI

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/cli.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="TOR-001"
TEST_NAME="/search przez CLI - wyszukiwanie torrentów"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Execute
log_step "Wywołanie download.search capability z query='ubuntu'"
START_TIME=$(date +%s.%N)
RESPONSE=$(cli_call "download.search" "query=ubuntu")
END_TIME=$(date +%s.%N)
RESPONSE_TIME=$(echo "$END_TIME - $START_TIME" | bc 2>/dev/null || echo "0")

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "Brak odpowiedzi z CLI"
fi

log_step "Odpowiedź otrzymana (${RESPONSE_TIME}s)"
echo "$RESPONSE" | head -40

# Verify
log_step "Weryfikacja odpowiedzi"

assert_json_valid "$RESPONSE" "Odpowiedź to valid JSON" || exit_test "$TEST_ID" "FAIL" "Invalid JSON"
assert_json_contains "$RESPONSE" "results" "Odpowiedź zawiera 'results'" || exit_test "$TEST_ID" "FAIL" "Brak 'results'"
assert_json_count "$RESPONSE" ".RawResult.CapabilityResult.Data.results" "1" "Znaleziono przynajmniej 1 wynik" || exit_test "$TEST_ID" "FAIL" "Brak wyników"
assert_response_time 30 "Czas odpowiedzi < 30s" || exit_test "$TEST_ID" "FAIL" "Zbyt wolna odpowiedź"

# Report
exit_test "$TEST_ID" "PASS" "Wszystkie asercje przeszły"
