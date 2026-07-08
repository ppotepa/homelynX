#!/bin/bash
# E2E Test Runner

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "${SCRIPT_DIR}/helpers/common.sh"

# Parse arguments
LIVE_MODE=false
TEST_PATTERN="*"
DOMAIN=""
RESULTS_DIR="${SCRIPT_DIR}/results"
TESTS_DIR="${SCRIPT_DIR}/tests"
mkdir -p "$RESULTS_DIR"

while [[ $# -gt 0 ]]; do
    case $1 in
        --live)
            LIVE_MODE=true
            shift
            ;;
        --help|-h)
            echo "Usage: ./run-tests.sh [OPTIONS] [PATTERN] [DOMAIN]"
            echo ""
            echo "Options:"
            echo "  --live          Use live environment from project .env file"
            echo "  --help, -h      Show this help message"
            echo ""
            echo "Examples:"
            echo "  ./run-tests.sh                    # Run all tests with mock config"
            echo "  ./run-tests.sh --live             # Run all tests with live config"
            echo "  ./run-tests.sh --live SYS-001     # Run specific test with live config"
            echo "  ./run-tests.sh '*' system         # Run all system tests"
            echo "  ./run-tests.sh --live '*' downloads  # Run all download tests with live config"
            exit 0
            ;;
        *)
            if [[ -z "$TEST_PATTERN_SET" ]]; then
                TEST_PATTERN="$1"
                TEST_PATTERN_SET=true
            else
                DOMAIN="$1"
            fi
            shift
            ;;
    esac
done

# Load configuration
if [[ "$LIVE_MODE" == true ]]; then
    log_info "Loading LIVE configuration from project .env"
    
    # Load only specific variables we need (avoids issues with unquoted values)
    ENV_FILE="/home/ppotepa/git/torrent-bot2/.env"
    
    export TELEGRAM_BOT_TOKEN=$(grep '^TELEGRAM_BOT_TOKEN=' "$ENV_FILE" | cut -d'=' -f2-)
    export TEST_CHAT_ID=$(grep '^TELEGRAM_ADMIN_CHAT_ID=' "$ENV_FILE" | cut -d'=' -f2-)
    [[ -z "$TEST_CHAT_ID" ]] && export TEST_CHAT_ID="8153696940"
    
    # qBittorrent - construct URL from components
    QBIT_HOST=$(grep '^QBIT_HOST=' "$ENV_FILE" | cut -d'=' -f2-)
    QBIT_PORT=$(grep '^QBIT_PORT=' "$ENV_FILE" | cut -d'=' -f2-)
    QBIT_HTTPS=$(grep '^QBIT_HTTPS=' "$ENV_FILE" | cut -d'=' -f2-)
    [[ "$QBIT_HTTPS" == "true" ]] && QBIT_PROTO="https" || QBIT_PROTO="http"
    export QBITTORRENT_URL="${QBIT_PROTO}://${QBIT_HOST}:${QBIT_PORT}"
    export QBITTORRENT_USER=$(grep '^QBIT_USERNAME=' "$ENV_FILE" | cut -d'=' -f2-)
    export QBITTORRENT_PASS=$(grep '^QBIT_PASSWORD=' "$ENV_FILE" | cut -d'=' -f2-)
    
    # Jackett - construct URL from components
    JACKETT_HOST=$(grep '^JACKETT_HOST=' "$ENV_FILE" | cut -d'=' -f2-)
    JACKETT_PORT=$(grep '^JACKETT_PORT=' "$ENV_FILE" | cut -d'=' -f2-)
    JACKETT_HTTPS=$(grep '^JACKETT_HTTPS=' "$ENV_FILE" | cut -d'=' -f2-)
    [[ "$JACKETT_HTTPS" == "true" ]] && JACKETT_PROTO="https" || JACKETT_PROTO="http"
    export JACKETT_URL="${JACKETT_PROTO}://${JACKETT_HOST}:${JACKETT_PORT}"
    export JACKETT_API_KEY=$(grep '^JACKETT_API_KEY=' "$ENV_FILE" | cut -d'=' -f2-)

    export TEST_ENDPOINT="${TEST_ENDPOINT:-http://localhost:5000}"
    export TORRENTBOT_TEST_ENDPOINT_SECRET=$(grep '^TORRENTBOT_TEST_ENDPOINT_SECRET=' "$ENV_FILE" | cut -d'=' -f2-)
    export TEST_ENDPOINT_SECRET="${TEST_ENDPOINT_SECRET:-$TORRENTBOT_TEST_ENDPOINT_SECRET}"
    
    log_success "Live configuration loaded"
    log_info "  Bot Token: ${TELEGRAM_BOT_TOKEN:0:10}..."
    log_info "  Chat ID: $TEST_CHAT_ID"
    log_info "  qBittorrent: $QBITTORRENT_URL"
    log_info "  Jackett: $JACKETT_URL"
else
    log_info "Loading mock configuration from config.env"
    source "${SCRIPT_DIR}/config.env"
    export TEST_ENDPOINT="${TEST_ENDPOINT:-http://localhost:5000}"
    export TEST_ENDPOINT_SECRET="${TEST_ENDPOINT_SECRET:-${TORRENTBOT_TEST_ENDPOINT_SECRET:-}}"
    export TORRENTBOT_TEST_ENDPOINT_SECRET="${TORRENTBOT_TEST_ENDPOINT_SECRET:-$TEST_ENDPOINT_SECRET}"
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo -e "${BLUE}🚀 Homelynx E2E Test Runner${NC}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

if [[ -n "$DOMAIN" ]]; then
    echo -e "${BLUE}Running tests for domain:${NC} $DOMAIN"
    TEST_FILES="${TESTS_DIR}/${DOMAIN}/${TEST_PATTERN}.sh"
else
    echo -e "${BLUE}Running all tests matching:${NC} $TEST_PATTERN"
    TEST_FILES="${TESTS_DIR}/*/${TEST_PATTERN}.sh"
fi

echo ""

# Run tests
for test_file in $TEST_FILES; do
    if [[ ! -f "$test_file" ]]; then
        continue
    fi
    
    TOTAL=$((TOTAL + 1))
    test_name=$(basename "$test_file" .sh)
    
    echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo -e "${BLUE}Test ${TOTAL}:${NC} $test_name"
    echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    
    start_timer
    
    if bash "$test_file"; then
        PASSED=$((PASSED + 1))
        stop_timer
        echo -e "${GREEN}✅ PASS${NC} (${TEST_DURATION}s)"
    else
        FAILED=$((FAILED + 1))
        stop_timer
        echo -e "${RED}❌ FAIL${NC} (${TEST_DURATION}s)"
    fi
    
    echo ""
done

# Generate summary
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo -e "${BLUE}📊 Test Summary${NC}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo -e "Total:   ${TOTAL}"
echo -e "${GREEN}Passed:  ${PASSED}${NC}"
echo -e "${RED}Failed:  ${FAILED}${NC}"
echo -e "${YELLOW}Skipped: ${SKIPPED}${NC}"
echo ""

# Calculate pass rate
if [[ $TOTAL -gt 0 ]]; then
    PASS_RATE=$(echo "scale=2; ($PASSED / $TOTAL) * 100" | bc)
    echo -e "Pass Rate: ${PASS_RATE}%"
fi

echo ""

# Generate HTML report
generate_html_report() {
    local output_file="$1"
    
    cat > "$output_file" <<EOF
<!DOCTYPE html>
<html>
<head>
    <title>E2E Test Results</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        .summary { background: #f0f0f0; padding: 20px; border-radius: 5px; margin-bottom: 20px; }
        .pass { color: green; }
        .fail { color: red; }
        .test { margin: 10px 0; padding: 10px; border: 1px solid #ddd; border-radius: 3px; }
        .test.pass { background: #e8f5e9; }
        .test.fail { background: #ffebee; }
    </style>
</head>
<body>
    <h1>Homelynx E2E Test Results</h1>
    <div class="summary">
        <h2>Summary</h2>
        <p>Total: $TOTAL</p>
        <p class="pass">Passed: $PASSED</p>
        <p class="fail">Failed: $FAILED</p>
        <p>Pass Rate: ${PASS_RATE}%</p>
        <p>Timestamp: $(date -u +%Y-%m-%dT%H:%M:%SZ)</p>
    </div>
    <h2>Test Results</h2>
EOF
    
    for result_file in "${RESULTS_DIR}"/*.json; do
        if [[ -f "$result_file" ]]; then
            local test_id=$(jq -r '.test_id' "$result_file")
            local status=$(jq -r '.status' "$result_file")
            local message=$(jq -r '.message' "$result_file")
            local timestamp=$(jq -r '.timestamp' "$result_file")
            
            local status_class="pass"
            [[ "$status" == "FAIL" ]] && status_class="fail"
            
            cat >> "$output_file" <<EOF
    <div class="test $status_class">
        <strong>$test_id</strong> - $status<br>
        <small>$message</small><br>
        <small>$timestamp</small>
    </div>
EOF
        fi
    done
    
    cat >> "$output_file" <<EOF
</body>
</html>
EOF
}

generate_html_report "${RESULTS_DIR}/summary.html"

echo -e "${BLUE}📄 HTML Report:${NC} ${RESULTS_DIR}/summary.html"
echo ""

# Exit with appropriate code
if [[ $FAILED -gt 0 ]]; then
    exit 1
else
    exit 0
fi
