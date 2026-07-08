# Homelynx E2E Tests

Automated end-to-end tests for the Homelynx Telegram bot.

## Structure

```
e2e-tests/
├── config.env                    # Configuration (tokens, URLs, etc.)
├── run-tests.sh                  # Test runner
├── helpers/
│   ├── common.sh                 # Common functions (logging, cleanup)
│   ├── telegram.sh               # Telegram API interactions
│   ├── qbittorrent.sh            # qBittorrent API interactions
│   └── assertions.sh             # Test assertions
├── tests/
│   ├── system/                   # System domain tests
│   │   ├── SYS-001-help.sh
│   │   └── SYS-002-health.sh
│   ├── downloads/                # Downloads domain tests
│   │   ├── DL-001-downloads.sh
│   │   ├── DL-005-download-url.sh
│   │   ├── DL-ERR-001-invalid-url.sh
│   │   └── DL-LANG-001-polish.sh
│   ├── jobs/                     # Jobs domain tests
│   ├── media/                    # Media domain tests
│   ├── torrent/                  # Torrent domain tests
│   └── bot/                      # Bot domain tests
└── results/                      # Test results (JSON + HTML)
```

## Prerequisites

1. **Docker containers running**:
   ```bash
   docker compose up -d
   ```

2. **Configuration**:
   Edit `config.env` and set:
   - `TELEGRAM_BOT_TOKEN` - Your Telegram bot token
   - `TEST_CHAT_ID` - Your Telegram user ID
   - `QBITTORRENT_URL` - qBittorrent Web UI URL
   - `QBITTORRENT_USER` - qBittorrent username
   - `QBITTORRENT_PASS` - qBittorrent password
   - `JACKETT_URL` - Jackett URL
   - `JACKETT_API_KEY` - Jackett API key

3. **Dependencies**:
   - `curl` - HTTP requests
   - `jq` - JSON parsing
   - `bc` - Math operations

## Usage

### Run with live environment (recommended)
```bash
./run-tests.sh --live                    # Run all tests with live config from .env
./run-tests.sh --live SYS-001            # Run specific test with live config
./run-tests.sh --live '*' downloads      # Run all download tests with live config
```

### Run with mock configuration
```bash
./run-tests.sh                           # Run all tests with mock config
./run-tests.sh "SYS-001"                 # Run specific test
./run-tests.sh '*' system                # Run all system tests
./run-tests.sh "DL-*"                    # Run all download tests
```

### Show help
```bash
./run-tests.sh --help
```

## Test Categories

### Category 1: Basic Command Tests
Simple slash commands and NL queries that return information.

### Category 2: State Verification Tests
Tests that verify system state changes after actions (e.g., torrent added to qBittorrent).

### Category 3: Full Workflow Tests
Multi-step scenarios that complete a full task (search → select → download → monitor).

### Category 4: Context Persistence Tests
Tests that verify conversation context is maintained across queries.

### Category 5: Error Handling & Recovery Tests
Tests for error conditions and recovery.

### Category 6: Multi-Language Tests
Tests in Polish, English, and mixed languages.

## Writing New Tests

1. Create a new test file in the appropriate domain directory:
   ```bash
   tests/downloads/DL-XXX-test-name.sh
   ```

2. Use the template:
   ```bash
   #!/bin/bash
   # DL-XXX: Test description
   
   source "$(dirname "$0")/../../helpers/common.sh"
   source "$(dirname "$0")/../../helpers/telegram.sh"
   source "$(dirname "$0")/../../helpers/qbittorrent.sh"
   source "$(dirname "$0")/../../helpers/assertions.sh"
   
   TEST_ID="DL-XXX"
   TEST_NAME="Test description"
   
   # Start test
   log_test_start "$TEST_ID" "$TEST_NAME"
   start_timer
   
   # Prerequisites
   check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"
   
   # Execute
   RESPONSE=$(send_telegram_command "/your-command")
   stop_timer
   
   # Verify
   assert_contains "$RESPONSE" "expected text" "Assertion message"
   assert_response_time 3 "Response time < 3s"
   
   # Report
   exit_test "$TEST_ID" "PASS" "All assertions passed"
   ```

3. Make executable:
   ```bash
   chmod +x tests/downloads/DL-XXX-test-name.sh
   ```

## Test Results

Results are stored in `results/` directory:
- `results/SYS-001.json` - Individual test result (JSON)
- `results/summary.html` - HTML report with all results

## Available Assertions

```bash
assert_contains "$haystack" "$needle" "message"
assert_not_contains "$haystack" "$needle" "message"
assert_equals "$expected" "$actual" "message"
assert_response_time 3 "message"
assert_greater_than "$actual" "$threshold" "message"
assert_less_than "$actual" "$threshold" "message"
assert_matches "$haystack" "$pattern" "message"
assert_file_exists "$filepath" "message"
assert_file_not_exists "$filepath" "message"
assert_dir_exists "$dirpath" "message"
assert_json_field "$json" ".field" "expected" "message"
assert_not_empty "$value" "message"
```

## Helper Functions

### Common
```bash
log_info "message"
log_success "message"
log_error "message"
log_step "message"
check_bot_running
check_qbittorrent_running
check_jackett_running
```

### Telegram
```bash
send_telegram_command "/command"
send_telegram_message "message"
get_last_bot_response
```

### qBittorrent
```bash
qbittorrent_get_torrents
qbittorrent_get_torrent_by_name "name"
qbittorrent_add_torrent_url "url"
qbittorrent_pause_torrent "hash"
qbittorrent_resume_torrent "hash"
qbittorrent_delete_torrent "hash" "delete_files"
```

## Troubleshooting

### Tests fail with "Bot not running"
- Check if `TELEGRAM_BOT_TOKEN` is correct in `config.env`
- Verify bot is running: `docker logs homelynx-bot`

### Tests fail with "qBittorrent not running"
- Check if `QBITTORRENT_URL` is correct in `config.env`
- Verify qBittorrent is accessible: `curl http://localhost:8080`

### Tests timeout
- Increase `DEFAULT_TIMEOUT` in `config.env`
- Check network connectivity

## Next Steps

1. Add more tests for remaining domains (jobs, media, torrent, bot)
2. Add Medium and Advanced tests
3. Integrate with CI/CD pipeline
4. Add performance benchmarks
