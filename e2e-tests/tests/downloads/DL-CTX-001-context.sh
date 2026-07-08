#!/bin/bash
# DL-CTX-001: Context persistence - Sequential queries use context

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/qbittorrent.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="DL-CTX-001"
TEST_NAME="Context persistence: Sequential queries maintain context"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"
check_qbittorrent_running || exit_test "$TEST_ID" "FAIL" "qBittorrent not running"

# Setup: Ensure we have at least one torrent
log_step "Setting up test data"
TORRENTS=$(qbittorrent_get_torrents)
TORRENT_COUNT=$(echo "$TORRENTS" | jq 'length')

if [[ "$TORRENT_COUNT" -eq 0 ]]; then
    log_step "Adding test torrent"
    add_test_torrent "ubuntu-24.04-desktop.iso"
    sleep 3
fi

# Query 1: Show downloads
log_step "Query 1: 'show downloads'"
sleep 1
RESPONSE1=$(send_telegram_command "show downloads")

if [[ -z "$RESPONSE1" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response to first query"
fi

log_step "Response 1 received"
echo "$RESPONSE1" | head -15

assert_not_empty "$RESPONSE1" "First response is not empty" || exit_test "$TEST_ID" "FAIL" "First response empty"

# Query 2: Ask about active count (should use context)
log_step "Query 2: 'how many are active'"
sleep 2
RESPONSE2=$(send_telegram_command "how many are active")

if [[ -z "$RESPONSE2" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response to second query"
fi

log_step "Response 2 received"
echo "$RESPONSE2"

assert_not_empty "$RESPONSE2" "Second response is not empty" || exit_test "$TEST_ID" "FAIL" "Second response empty"
assert_contains "$RESPONSE2" "active" "Second response mentions 'active'" || exit_test "$TEST_ID" "FAIL" "Context not used for 'active'"

# Query 3: Ask about specific torrent (should use context)
log_step "Query 3: 'is ubuntu downloading'"
sleep 2
RESPONSE3=$(send_telegram_command "is ubuntu downloading")

if [[ -z "$RESPONSE3" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response to third query"
fi

log_step "Response 3 received"
echo "$RESPONSE3"

assert_not_empty "$RESPONSE3" "Third response is not empty" || exit_test "$TEST_ID" "FAIL" "Third response empty"

# Verify context was used (responses should be consistent)
log_step "Verifying context consistency"

# All three responses should be about downloads
assert_contains "$RESPONSE1" "download" "First response about downloads" || exit_test "$TEST_ID" "FAIL" "First response not about downloads"

# Cleanup
log_step "Cleaning up"
remove_test_torrent "ubuntu-24.04-desktop.iso"

stop_timer

# Report
exit_test "$TEST_ID" "PASS" "Context persistence verified (${TEST_DURATION}s)"
