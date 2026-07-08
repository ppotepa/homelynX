#!/bin/bash
# qBittorrent API helper functions

source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

# Login to qBittorrent API
qbittorrent_login() {
    local response=$(curl -s -X POST "${QBITTORRENT_URL}/api/v2/auth/login" \
        -d "username=${QBITTORRENT_USER}&password=${QBITTORRENT_PASS}")
    
    if [[ "$response" == "Ok." ]]; then
        return 0
    else
        log_error "Failed to login to qBittorrent: $response"
        return 1
    fi
}

# Get all torrents
qbittorrent_get_torrents() {
    curl -s "${QBITTORRENT_URL}/api/v2/torrents/info"
}

# Get torrent by name
qbittorrent_get_torrent_by_name() {
    local name="$1"
    local torrents=$(qbittorrent_get_torrents)
    echo "$torrents" | jq -r --arg name "$name" '.[] | select(.name == $name)'
}

# Add torrent from URL
qbittorrent_add_torrent_url() {
    local url="$1"
    local response=$(curl -s -X POST "${QBITTORRENT_URL}/api/v2/torrents/add" \
        -d "urls=${url}")
    
    if [[ "$response" == "Ok." ]]; then
        return 0
    else
        log_error "Failed to add torrent: $response"
        return 1
    fi
}

# Pause torrent by hash
qbittorrent_pause_torrent() {
    local hash="$1"
    curl -s -X POST "${QBITTORRENT_URL}/api/v2/torrents/pause" \
        -d "hashes=${hash}" > /dev/null
}

# Resume torrent by hash
qbittorrent_resume_torrent() {
    local hash="$1"
    curl -s -X POST "${QBITTORRENT_URL}/api/v2/torrents/resume" \
        -d "hashes=${hash}" > /dev/null
}

# Delete torrent by hash
qbittorrent_delete_torrent() {
    local hash="$1"
    local delete_files="${2:-false}"
    curl -s -X POST "${QBITTORRENT_URL}/api/v2/torrents/delete" \
        -d "hashes=${hash}&deleteFiles=${delete_files}" > /dev/null
}

# Get torrent state
qbittorrent_get_torrent_state() {
    local name="$1"
    local torrent=$(qbittorrent_get_torrent_by_name "$name")
    echo "$torrent" | jq -r '.state'
}

# Get torrent progress
qbittorrent_get_torrent_progress() {
    local name="$1"
    local torrent=$(qbittorrent_get_torrent_by_name "$name")
    echo "$torrent" | jq -r '.progress'
}

# Get torrent download speed
qbittorrent_get_torrent_speed() {
    local name="$1"
    local torrent=$(qbittorrent_get_torrent_by_name "$name")
    echo "$torrent" | jq -r '.dlspeed'
}

# Add test torrent
add_test_torrent() {
    local name="$1"
    local state="${2:-downloading}"
    local progress="${3:-0}"
    
    log_step "Adding test torrent: $name"
    
    # Use a small test torrent URL
    local test_url="https://releases.ubuntu.com/24.04/ubuntu-24.04-desktop-amd64.iso.torrent"
    
    if qbittorrent_add_torrent_url "$test_url"; then
        log_success "Test torrent added"
        return 0
    else
        log_error "Failed to add test torrent"
        return 1
    fi
}

# Remove test torrent
remove_test_torrent() {
    local name="$1"
    
    log_step "Removing test torrent: $name"
    
    local torrent=$(qbittorrent_get_torrent_by_name "$name")
    local hash=$(echo "$torrent" | jq -r '.hash')
    
    if [[ -n "$hash" && "$hash" != "null" ]]; then
        qbittorrent_delete_torrent "$hash" "false"
        log_success "Test torrent removed"
        return 0
    else
        log_warning "Test torrent not found"
        return 0
    fi
}

# Export functions
export -f qbittorrent_login qbittorrent_get_torrents qbittorrent_get_torrent_by_name
export -f qbittorrent_add_torrent_url qbittorrent_pause_torrent qbittorrent_resume_torrent
export -f qbittorrent_delete_torrent qbittorrent_get_torrent_state qbittorrent_get_torrent_progress
export -f qbittorrent_get_torrent_speed add_test_torrent remove_test_torrent
