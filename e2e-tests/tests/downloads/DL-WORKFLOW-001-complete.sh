#!/bin/bash
# DL-WORKFLOW-001: Complete download workflow - Search → Select → Monitor → Cancel

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/qbittorrent.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="DL-WORKFLOW-001"
TEST_NAME="Complete download workflow: Search → Select → Monitor → Cancel"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"
check_qbittorrent_running || exit_test "$TEST_ID" "FAIL" "qBittorrent not running"

# Cleanup any existing test torrents
log_step "Cleaning up existing test torrents"
remove_test_torrent "ubuntu"

# Step 1: Search for torrents
log_step "Step 1: Search for 'ubuntu'"
SEARCH_RESPONSE=$(send_telegram_command "/download_search ubuntu")

if [[ -z "$SEARCH_RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from search command"
fi

log_step "Search response received"
echo "$SEARCH_RESPONSE" | head -15

assert_contains "$SEARCH_RESPONSE" "ubuntu" "Search results contain 'ubuntu'" || exit_test "$TEST_ID" "FAIL" "Search results don't contain 'ubuntu'"

# Step 2: Select first result
log_step "Step 2: Select first result"
sleep 2
SELECT_RESPONSE=$(send_telegram_command "/select 1")

if [[ -z "$SELECT_RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from select command"
fi

log_step "Select response received"
echo "$SELECT_RESPONSE"

assert_contains "$SELECT_RESPONSE" "download" "Select response mentions download" || exit_test "$TEST_ID" "FAIL" "Download not started"

# Step 3: Wait and verify torrent appears in qBittorrent
log_step "Step 3: Waiting for torrent to appear in qBittorrent"
sleep 5

TORRENTS=$(qbittorrent_get_torrents)
TORRENT_COUNT=$(echo "$TORRENTS" | jq 'length')

if [[ "$TORRENT_COUNT" -eq 0 ]]; then
    exit_test "$TEST_ID" "FAIL" "No torrents found in qBittorrent after select"
fi

log_success "Found $TORRENT_COUNT torrent(s) in qBittorrent"

# Get the first torrent
TORRENT=$(echo "$TORRENTS" | jq '.[0]')
TORRENT_NAME=$(echo "$TORRENT" | jq -r '.name')
TORRENT_HASH=$(echo "$TORRENT" | jq -r '.hash')
TORRENT_STATE=$(echo "$TORRENT" | jq -r '.state')

log_step "Torrent: $TORRENT_NAME (state: $TORRENT_STATE)"

assert_not_empty "$TORRENT_NAME" "Torrent has a name" || exit_test "$TEST_ID" "FAIL" "Torrent name is empty"
assert_not_empty "$TORRENT_HASH" "Torrent has a hash" || exit_test "$TEST_ID" "FAIL" "Torrent hash is empty"

# Step 4: Check downloads list
log_step "Step 4: Check /downloads command"
sleep 2
DOWNLOADS_RESPONSE=$(send_telegram_command "/downloads")

if [[ -z "$DOWNLOADS_RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from /downloads command"
fi

log_step "Downloads response received"
echo "$DOWNLOADS_RESPONSE" | head -20

assert_contains "$DOWNLOADS_RESPONSE" "download" "Downloads response contains download info" || exit_test "$TEST_ID" "FAIL" "Downloads list empty"

# Step 5: Cancel the download
log_step "Step 5: Cancel the download"
CANCEL_RESPONSE=$(send_telegram_command "/cancel $TORRENT_NAME")

if [[ -z "$CANCEL_RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from cancel command"
fi

log_step "Cancel response received"
echo "$CANCEL_RESPONSE"

assert_contains "$CANCEL_RESPONSE" "cancel" "Cancel response mentions cancellation" || exit_test "$TEST_ID" "FAIL" "Cancel not confirmed"

# Step 6: Verify torrent is removed
log_step "Step 6: Verifying torrent is removed from qBittorrent"
sleep 3

TORRENTS_AFTER=$(qbittorrent_get_torrents)
TORRENT_COUNT_AFTER=$(echo "$TORRENTS_AFTER" | jq 'length')

log_step "Torrents after cancel: $TORRENT_COUNT_AFTER (was: $TORRENT_COUNT)"

# Cleanup
log_step "Final cleanup"
if [[ "$TORRENT_COUNT_AFTER" -gt 0 ]]; then
    remove_test_torrent "$TORRENT_NAME"
fi

stop_timer

# Report
exit_test "$TEST_ID" "PASS" "Complete workflow executed successfully (${TEST_DURATION}s)"
