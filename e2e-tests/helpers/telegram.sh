#!/bin/bash
# Telegram API helper functions for E2E testing
# Uses test HTTP endpoint to inject Update objects directly into the bot

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/common.sh"

# Load config
if [ -z "$TELEGRAM_BOT_TOKEN" ]; then
    if [ -f "$SCRIPT_DIR/../config.env" ]; then
        source "$SCRIPT_DIR/../config.env"
    fi
fi

# Test endpoint URL and auth secret
TEST_ENDPOINT="${TEST_ENDPOINT:-http://localhost:5000}"
TEST_ENDPOINT_SECRET="${TEST_ENDPOINT_SECRET:-${TORRENTBOT_TEST_ENDPOINT_SECRET:-}}"

# Send command to bot via test endpoint and get response
send_telegram_command() {
    local command="$1"
    local timeout="${2:-60}"
    local chat_id="${TEST_CHAT_ID:-8153696940}"
    local user_id="${chat_id}"
    
    log_step "Sending Telegram command via test endpoint: $command" >&2
    
    # Build Update JSON
    local update_json=$(cat <<EOF
{
    "update_id": $(date +%s),
    "message": {
        "message_id": $(shuf -i 1000-9999 -n 1),
        "date": $(date +%s),
        "chat": {"id": $chat_id, "type": "private"},
        "from": {"id": $user_id, "is_bot": false, "first_name": "E2E Test"},
        "text": "$command"
    }
}
EOF
)
    
    local -a curl_headers=(-H "Content-Type: application/json")
    if [ -n "$TEST_ENDPOINT_SECRET" ]; then
        curl_headers+=(-H "X-TorrentBot-Test-Secret: ${TEST_ENDPOINT_SECRET}")
    else
        log_warning "TEST_ENDPOINT_SECRET not set; calling test endpoint without auth header" >&2
    fi

    # Send to test endpoint
    local response=$(curl -s -X POST "${TEST_ENDPOINT}/test/inject-update" \
        "${curl_headers[@]}" \
        -d "$update_json" \
        --max-time "$timeout")
    
    if [ -z "$response" ]; then
        log_error "No response from test endpoint"
        return 1
    fi
    
    # Check if request was successful
    local success=$(echo "$response" | jq -r '.success // false')
    if [ "$success" != "true" ]; then
        log_error "Test endpoint returned error"
        log_error "Response: $response"
        return 1
    fi
    
    # Extract response text
    local response_text=$(echo "$response" | jq -r '.response // "No response"')
    
    log_info "Response received: ${response_text:0:100}..." >&2
    echo "$response_text"
    return 0
}

# Send message without waiting for response (fire-and-forget)
send_telegram_message() {
    local message="$1"
    local chat_id="${2:-${TEST_CHAT_ID:-8153696940}}"
    
    # Just call send_telegram_command without waiting
    send_telegram_command "$message" 5 "$chat_id" > /dev/null 2>&1
}

# Get last bot response (not applicable for test endpoint, returns empty)
get_last_bot_response() {
    echo ""
}

export -f send_telegram_command send_telegram_message get_last_bot_response
