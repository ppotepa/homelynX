#!/bin/bash
# DL-005: /download url - Download from URL and verify in qBittorrent

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/qbittorrent.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="DL-005"
TEST_NAME="/download url - Download from URL and verify"

# Start test
log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"
check_qbittorrent_running || exit_test "$TEST_ID" "FAIL" "qBittorrent not running"

# Cleanup any existing test torrent
log_step "Cleaning up existing test torrent"
remove_test_torrent "ubuntu-24.04-desktop"

# Execute
TEST_URL="https://releases.ubuntu.com/24.04/ubuntu-24.04-desktop-amd64.iso.torrent"
log_step "Sending download command"
RESPONSE=$(send_telegram_command "/download url=${TEST_URL}")
stop_timer

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "No response from bot"
fi

log_step "Response received (${RESPONSE_TIME}s)"
echo "$RESPONSE"

# Verify bot response
log_step "Verifying bot response"
assert_contains "$RESPONSE" "download" "Response mentions download" || exit_test "$TEST_ID" "FAIL" "Download confirmation missing"
assert_response_time 5 "Response time < 5s" || exit_test "$TEST_ID" "FAIL" "Response too slow"

# Wait for torrent to appear in qBittorrent
log_step "Waiting for torrent to appear in qBittorrent"
sleep 3

# Verify torrent in qBittorrent
log_step "Verifying torrent in qBittorrent"
TORRENT=$(qbittorrent_get_torrent_by_name "ubuntu-24.04-desktop")
TORRENT_HASH=$(echo "$TORRENT" | jq -r '.hash')

if [[ -z "$TORRENT_HASH" || "$TORRENT_HASH" == "null" ]]; then
    exit_test "$TEST_ID" "FAIL" "Torrent not found in qBittorrent"
fi

log_success "Torrent found in qBittorrent: $TORRENT_HASH"

# Verify torrent state
TORRENT_STATE=$(echo "$TORRENT" | jq -r '.state')
log_step "Torrent state: $TORRENT_STATE"

if [[ "$TORRENT_STATE" == "error" ]]; then
    exit_test "$TEST_ID" "FAIL" "Torrent is in error state"
fi

# Cleanup
log_step "Cleaning up"
qbittorrent_delete_torrent "$TORRENT_HASH" "false"

# Report
exit_test "$TEST_ID" "PASS" "All assertions passed"
