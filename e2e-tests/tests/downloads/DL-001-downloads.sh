#!/bin/bash
# DL-001: /downloads - List active downloads

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/qbittorrent.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="DL-001"
TEST_NAME="/downloads - List active downloads"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"
check_qbittorrent_running || exit_test "$TEST_ID" "FAIL" "qBittorrent not running"

# Setup: Add a test torrent if none exist
log_step "Setting up test data"
TORRENTS=$(qbittorrent_get_torrents)
TORRENT_COUNT=$(echo "$TORRENTS" | jq 'length')

if [[ "$TORRENT_COUNT" -eq 0 ]]; then
    log_step "Adding test torrent"
    add_test_torrent "ubuntu-24.04-desktop.iso"
    sleep 3
fi

# Execute
log_step "Sending /downloads command"
RESPONSE=$(send_telegram_command "/downloads")
stop_timer

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from bot"
fi

log_step "Response received (${RESPONSE_TIME}s)"
echo "$RESPONSE" | head -30

# Verify
log_step "Verifying response"

assert_not_empty "$RESPONSE" "Response is not empty" || exit_test "$TEST_ID" "FAIL" "Empty response"
assert_response_time 3 "Response time < 3s" || exit_test "$TEST_ID" "FAIL" "Response too slow"

# Check if response contains torrent information
if [[ "$TORRENT_COUNT" -gt 0 ]]; then
    assert_contains "$RESPONSE" "download" "Response mentions downloads" || exit_test "$TEST_ID" "FAIL" "Download info missing"
fi

# Cleanup
log_step "Cleaning up"
remove_test_torrent "ubuntu-24.04-desktop.iso"

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
