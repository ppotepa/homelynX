#!/bin/bash
# Common helper functions for E2E tests

# E2E root (always helpers/.. regardless of caller SCRIPT_DIR)
E2E_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Load configuration (only if not already loaded by run-tests.sh)
if [[ -z "$SCRIPT_DIR" ]]; then
    SCRIPT_DIR="$E2E_DIR"
fi

if [[ -z "$TELEGRAM_BOT_TOKEN" && -f "${E2E_DIR}/config.env" ]]; then
    # shellcheck source=/dev/null
    source "${E2E_DIR}/config.env"
fi

export TEST_ENDPOINT="${TEST_ENDPOINT:-http://localhost:5000}"
export TEST_ENDPOINT_SECRET="${TEST_ENDPOINT_SECRET:-${TORRENTBOT_TEST_ENDPOINT_SECRET:-}}"
export TORRENTBOT_TEST_ENDPOINT_SECRET="${TORRENTBOT_TEST_ENDPOINT_SECRET:-$TEST_ENDPOINT_SECRET}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[PASS]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_step() {
    echo -e "${BLUE}[STEP]${NC} $1"
}

log_assertion() {
    local status="$1"
    local message="$2"
    if [[ "$status" == "PASS" ]]; then
        echo -e "  ${GREEN}✓${NC} $message"
    else
        echo -e "  ${RED}✗${NC} $message"
    fi
}

log_test_start() {
    local test_id="$1"
    local test_name="$2"
    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo -e "${BLUE}Starting Test:${NC} $test_id - $test_name"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
}

# Test result functions
exit_test() {
    local test_id="$1"
    local status="$2"
    local message="$3"
    local results_dir="${SCRIPT_DIR}/../results"
    
    mkdir -p "$results_dir"
    
    # Create JSON result
    cat > "${results_dir}/${test_id}.json" <<EOF
{
  "test_id": "$test_id",
  "status": "$status",
  "message": "$message",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "duration": "${TEST_DURATION:-0}s"
}
EOF
    
    if [[ "$status" == "PASS" ]]; then
        log_success "$message"
        echo ""
        return 0
    else
        log_error "$message"
        echo ""
        return 1
    fi
}

# Prerequisite checks
check_bot_running() {
    log_step "Checking if bot is running"
    if curl -s "https://api.telegram.org/bot${TELEGRAM_BOT_TOKEN}/getMe" > /dev/null 2>&1; then
        log_success "Bot is running"
        return 0
    else
        log_error "Bot is not running or token is invalid"
        return 1
    fi
}

check_qbittorrent_running() {
    log_step "Checking if qBittorrent is running"
    if curl -s "${QBITTORRENT_URL}/api/v2/app/version" > /dev/null 2>&1; then
        log_success "qBittorrent is running"
        return 0
    else
        log_error "qBittorrent is not running at ${QBITTORRENT_URL}"
        return 1
    fi
}

check_jackett_running() {
    log_step "Checking if Jackett is running"
    if curl -s "${JACKETT_URL}/api/v2.0/indexers" > /dev/null 2>&1; then
        log_success "Jackett is running"
        return 0
    else
        log_error "Jackett is not running at ${JACKETT_URL}"
        return 1
    fi
}

# Cleanup functions
cleanup_test_files() {
    local pattern="$1"
    log_step "Cleaning up test files matching: $pattern"
    find "${TEST_MEDIA_DIR}" -name "$pattern" -type f -delete 2>/dev/null || true
}

# Timing functions
start_timer() {
    TEST_START_TIME=$(date +%s.%N)
}

stop_timer() {
    local end_time=$(date +%s.%N)
    TEST_DURATION=$(echo "$end_time - $TEST_START_TIME" | bc)
}

# Export functions
export -f log_info log_success log_error log_warning log_step log_assertion
export -f log_test_start exit_test
export -f check_bot_running check_qbittorrent_running check_jackett_running
export -f cleanup_test_files start_timer stop_timer
