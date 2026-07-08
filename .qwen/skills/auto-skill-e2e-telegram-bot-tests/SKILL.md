---
name: e2e-telegram-bot-tests
description: Bash-based E2E test framework for Telegram bots with state verification, multi-category tests, and HTML reporting
source: auto-skill
extracted_at: '2026-07-06T17:23:50.325Z'
---

# E2E Test Framework for Telegram Bots

Bash-based E2E testing infrastructure that verifies bot behavior end-to-end through the Telegram API, with external state verification (e.g., qBittorrent API) and detailed test categorization.

## Directory Structure

```
e2e-tests/
├── config.env                    # Tokens, URLs, timeouts
├── run-tests.sh                  # Test runner with HTML report generation
├── helpers/
│   ├── common.sh                 # Logging, timers, prerequisite checks
│   ├── telegram.sh               # Send commands via Telegram Bot API, poll responses
│   ├── qbittorrent.sh            # qBittorrent Web API (login, add/pause/resume/delete)
│   └── assertions.sh             # 15+ assertion functions
├── tests/
│   ├── system/                   # SYS-001, SYS-002, ...
│   ├── downloads/                # DL-001, DL-005, DL-ERR-001, DL-LANG-001, ...
│   ├── jobs/
│   ├── media/
│   ├── torrent/
│   └── bot/
└── results/                      # JSON per test + summary.html
```

## Test Categories

| Category | Purpose | Example |
|----------|---------|---------|
| **Basic Command** | Slash commands return expected output | `/help` shows all modules |
| **State Verification** | Verify system state via external API | `/download url=...` → check qBittorrent API |
| **Full Workflow** | Multi-step end-to-end scenarios | Search → Select → Download → Monitor → Cancel |
| **Context Persistence** | Conversation context maintained across queries | "show downloads" → "how many active" uses context |
| **Error Handling** | Invalid inputs produce clear errors | `/download url=invalid` → error message |
| **Multi-Language** | NL works in Polish, English, mixed | "pokaż pobierania" → same as /downloads |
| **Idempotency** | Repeated actions handled correctly | Pause already-paused torrent → error |

## Key Pattern: State Verification

Don't just check the bot's text response — verify the actual system state:

```bash
# 1. Send command to bot
RESPONSE=$(send_telegram_command "/download url=${TEST_URL}")

# 2. Wait for side effect
sleep 3

# 3. Verify via external API
TORRENT=$(qbittorrent_get_torrent_by_name "ubuntu-24.04-desktop")
TORRENT_HASH=$(echo "$TORRENT" | jq -r '.hash')

if [[ -z "$TORRENT_HASH" || "$TORRENT_HASH" == "null" ]]; then
    exit_test "$TEST_ID" "FAIL" "Torrent not found in qBittorrent"
fi
```

## Telegram Helper: Send & Poll

```bash
send_telegram_command() {
    local command="$1"

    # Get latest update ID before sending
    local updates_before=$(curl -s "https://api.telegram.org/bot${TOKEN}/getUpdates?offset=-1" \
        | jq -r '.result[-1].update_id // 0')

    # Send the command
    local send_response=$(curl -s -X POST "https://api.telegram.org/bot${TOKEN}/sendMessage" \
        -H "Content-Type: application/json" \
        -d "{\"chat_id\": ${CHAT_ID}, \"text\": \"${command}\"}")

    local message_id=$(echo "$send_response" | jq -r '.result.message_id')

    # Poll for bot response (reply_to_message matches our message_id)
    local max_attempts=30
    for ((i=0; i<max_attempts; i++)); do
        sleep 0.5
        local updates=$(curl -s "https://api.telegram.org/bot${TOKEN}/getUpdates?offset=${updates_before}")
        local bot_response=$(echo "$updates" | jq -r --arg msg_id "$message_id" '
            .result[] |
            select(.message.reply_to_message.message_id == ($msg_id | tonumber)) |
            .message.text
        ' | head -1)

        [[ -n "$bot_response" && "$bot_response" != "null" ]] && echo "$bot_response" && return 0
    done
}
```

## Test Template

```bash
#!/bin/bash
# DL-XXX: Test description

source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/telegram.sh"
source "$(dirname "$0")/../../helpers/qbittorrent.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="DL-XXX"
TEST_NAME="Description"

log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Prerequisites
check_bot_running || exit_test "$TEST_ID" "FAIL" "Bot not running"
check_qbittorrent_running || exit_test "$TEST_ID" "FAIL" "qBittorrent not running"

# Setup (if needed)
add_test_torrent "test-file.iso"

# Execute
RESPONSE=$(send_telegram_command "/your-command")
stop_timer

# Verify
assert_contains "$RESPONSE" "expected text" "Contains expected text"
assert_response_time 3 "Response time < 3s"

# Cleanup
remove_test_torrent "test-file.iso"

exit_test "$TEST_ID" "PASS" "All assertions passed"
```

## Test Runner Usage

```bash
./run-tests.sh                  # All tests
./run-tests.sh "*" system       # All system domain tests
./run-tests.sh "SYS-001"        # Single test
./run-tests.sh "DL-ERR-*"       # Pattern match
```

## Available Assertions

```bash
assert_contains "$haystack" "$needle" "message"
assert_not_contains "$haystack" "$needle" "message"
assert_equals "$expected" "$actual" "message"
assert_response_time 3 "message"
assert_greater_than "$actual" "$threshold" "message"
assert_less_than "$actual" "$threshold" "message"
assert_matches "$haystack" "$regex_pattern" "message"
assert_file_exists "$filepath" "message"
assert_file_not_exists "$filepath" "message"
assert_json_field "$json" ".field" "expected" "message"
assert_not_empty "$value" "message"
```

## Pitfalls

### 🚨 CRITICAL: Telegram Bot API Cannot Send Messages as User

**The Problem:**
The Telegram Bot API's `sendMessage` endpoint sends messages **AS the bot**, not as a user. The bot's `getUpdates` polling only receives messages **FROM users**, not from the bot itself. This means:

```bash
# This sends a message FROM the bot TO the chat
curl -X POST "https://api.telegram.org/bot<TOKEN>/sendMessage" \
  -d '{"chat_id": <CHAT_ID>, "text": "/ping"}'

# The bot will NOT see this in getUpdates because it's from the bot, not a user
curl "https://api.telegram.org/bot<TOKEN>/getUpdates"  # Returns empty!
```

**Impact on E2E Tests:**
- Automated tests **cannot** send commands to the bot via Bot API
- Tests that use `sendMessage` to simulate user input will hang waiting for responses
- The bot's polling loop will never see these messages

**Solutions (in order of preference):**

1. **Test-Only HTTP Endpoint** (RECOMMENDED) — Add a test endpoint to the bot that accepts synthetic Update JSON:
   ```bash
   # Add POST /test/inject-update endpoint to Telegram bot
   # Endpoint accepts Update JSON and returns response
   curl -X POST http://localhost:5000/test/inject-update \
     -H "Content-Type: application/json" \
     -d '{
       "update_id": 1,
       "message": {
         "message_id": 1,
         "from": {"id": 8153696940, "is_bot": false},
         "chat": {"id": 8153696940, "type": "private"},
         "text": "/health"
       }
     }'
   ```
   **Advantages:**
   - Tests full stack (Telegram transport → adapter → engine)
   - No interactive authentication needed
   - Reliable for CI/CD
   - Fast response (synchronous HTTP)
   - No external dependencies
   
   **Implementation:**
   ```csharp
   // In Telegram bot Program.cs (Development environment only)
   if (app.Environment.IsDevelopment())
   {
       app.MapPost("/test/inject-update", async (HttpContext ctx, TelegramBotClient bot) =>
       {
           var update = await ctx.Request.ReadFromJsonAsync<Update>();
           var response = await adapter.HandleUpdateAsync(update, ctx.RequestAborted);
           return Results.Json(new { success = true, response });
       });
   }
   ```

2. **Telegram User API (Telethon/Pyrogram)** — Use a user account to send messages:
   ```python
   # Python example with Telethon
   from telethon import TelegramClient
   
   client = TelegramClient('session', api_id, api_hash)
   await client.send_message(bot_username, '/ping')
   ```
   Requires: User phone number, API credentials from my.telegram.org

2. **Manual Testing Mode** - Human sends commands, tests only verify:
   ```bash
   # Human sends /ping manually in Telegram
   # Test script polls for response and verifies
   RESPONSE=$(poll_bot_response "pong" timeout=10)
   assert_contains "$RESPONSE" "pong"
   ```

3. **Mock/Stub Mode** - Bypass Telegram entirely, test engine directly:
   ```bash
   # Call engine API directly
   RESPONSE=$(curl -s http://localhost:5000/api/execute -d '{"command": "/ping"}')
   assert_contains "$RESPONSE" "pong"
   ```

4. **Webhook Testing** - Simulate webhook payloads:
   ```bash
   curl -X POST http://localhost:5000/webhook \
     -H "Content-Type: application/json" \
     -d '{"update_id": 123, "message": {"text": "/ping", "from": {"id": 8153696940}}}'
   ```

**Recommended Approach:**
For full E2E testing, implement a **test-only HTTP endpoint** (`POST /test/inject-update`) that accepts synthetic Update JSON. This tests the complete Telegram transport layer without authentication issues.

For quick development feedback, use **CLI-based testing** which bypasses Telegram entirely and tests the engine directly.

### ✅ CLI-Based E2E Testing (Recommended)

The CLI adapter (`TorrentBot.Adapters.Cli`) provides direct engine access without Telegram. This is the fastest, most reliable testing approach.

**CLI Commands:**
```bash
# List capabilities
dotnet TorrentBot.Adapters.Cli.dll capabilities list --json

# Call a capability
dotnet TorrentBot.Adapters.Cli.dll capability call bot.ping --json
dotnet TorrentBot.Adapters.Cli.dll capability call download.list --json
dotnet TorrentBot.Adapters.Cli.dll capability call download.search --param query=ubuntu --json

# Natural language (requires LLM)
dotnet TorrentBot.Adapters.Cli.dll agent run "pokaż capabilities" --json

# Query data sources
dotnet TorrentBot.Adapters.Cli.dll query downloads --json
dotnet TorrentBot.Adapters.Cli.dll query media_files --where "type=:movie" --json
```

**CLI Helper (helpers/cli.sh):**
```bash
#!/bin/bash
CLI_PATH="/path/to/TorrentBot.Adapters.Cli"

cli_call() {
    local capability="$1"
    shift
    local params=("$@")
    log_step "Calling capability: $capability" >&2  # stderr for logs

    local cmd="$CLI_PATH capability call $capability"
    for param in "${params[@]}"; do
        cmd="$cmd --param $param"
    done
    cmd="$cmd --json"

    START_TIME=$(date +%s.%N)
    RESPONSE=$(eval "$cmd" 2>/dev/null)  # suppress stderr (logs)
    END_TIME=$(date +%s.%N)
    RESPONSE_TIME=$(echo "$END_TIME - $START_TIME" | bc 2>/dev/null || echo "0")

    echo "$RESPONSE"  # stdout = JSON only
    return 0
}

cli_list() {
    log_step "Listing capabilities" >&2
    START_TIME=$(date +%s.%N)
    RESPONSE=$($CLI_PATH capabilities list --json 2>/dev/null)
    END_TIME=$(date +%s.%N)
    RESPONSE_TIME=$(echo "$END_TIME - $START_TIME" | bc 2>/dev/null || echo "0")
    echo "$RESPONSE"
    return 0
}

cli_agent_run() {
    local text="$1"
    log_step "Running agent: $text" >&2
    START_TIME=$(date +%s.%N)
    RESPONSE=$($CLI_PATH agent run "$text" --json 2>/dev/null)
    END_TIME=$(date +%s.%N)
    RESPONSE_TIME=$(echo "$END_TIME - $START_TIME" | bc 2>/dev/null || echo "0")
    echo "$RESPONSE"
    return 0
}
```

**Critical: Redirect stderr to /dev/null**
The CLI outputs engine initialization logs to stderr. When capturing JSON output, redirect stderr:
```bash
RESPONSE=$($CLI_PATH capabilities list --json 2>/dev/null)
```

**Critical: Log to stderr, data to stdout**
Helper functions must send log messages to stderr (`>&2`) so they don't pollute the JSON output captured by the caller.

**Critical: Load project .env for CLI tests**
The CLI adapter reads configuration from environment variables. When running tests outside Docker, you must load the project's `.env` file. Use `set -a` to export all variables:

```bash
# In helpers/cli.sh - load project .env
PROJECT_ENV="$(dirname "${BASH_SOURCE[0]}")/../../.env"
if [[ -f "$PROJECT_ENV" ]]; then
    set -a
    source "$PROJECT_ENV"
    set +a
fi
```

**Why `set -a`?** Without it, `source .env` sets shell variables but doesn't export them. The .NET CLI process won't see them via `Environment.GetEnvironmentVariable()`. The `set -a` / `set +a` pattern exports all variables set between the two commands.

**Critical: JSON path for CLI capability output**
The CLI wraps capability results in a nested structure. When asserting on results, use the full path:

```bash
# ❌ WRONG - path doesn't exist in CLI output
assert_json_count "$RESPONSE" ".results" "1"

# ✅ CORRECT - full path through CLI wrapper
assert_json_count "$RESPONSE" ".RawResult.CapabilityResult.Data.results" "1"
```

The CLI JSON structure is:
```json
{
  "Success": true,
  "RawResult": {
    "Success": true,
    "CapabilityResult": {
      "Success": true,
      "Data": {
        "results": [...],
        "count": 221
      },
      "Message": "Found 221 result(s)"
    }
  }
}
```

**CLI Test Template:**
```bash
#!/bin/bash
source "$(dirname "$0")/../../helpers/common.sh"
source "$(dirname "$0")/../../helpers/cli.sh"
source "$(dirname "$0")/../../helpers/assertions.sh"

TEST_ID="SYS-001"
TEST_NAME="Lista capabilities przez CLI"

log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Execute
START_TIME=$(date +%s.%N)
RESPONSE=$(cli_list)
END_TIME=$(date +%s.%N)
RESPONSE_TIME=$(echo "$END_TIME - $START_TIME" | bc 2>/dev/null || echo "0")

if [[ -z "$RESPONSE" ]]; then
    exit_test "$TEST_ID" "FAIL" "Brak odpowiedzi z CLI"
fi

# Verify
assert_json_valid "$RESPONSE" "Odpowiedź to valid JSON" || exit_test "$TEST_ID" "FAIL" "Invalid JSON"
assert_json_count "$RESPONSE" ".capabilities" "50" "Lista zawiera capabilities"
assert_json_contains "$RESPONSE" "system.help" "Zawiera system.help"
assert_response_time 3 "Czas odpowiedzi < 3s"

exit_test "$TEST_ID" "PASS" "Wszystkie asercje przeszły"
```

**JSON Assertions for CLI:**
```bash
assert_json_valid() {
    local json="$1"
    local message="${2:-JSON is valid}"
    if echo "$json" | jq empty 2>/dev/null; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        return 1
    fi
}

assert_json_contains() {
    local json="$1"
    local value="$2"
    local message="${3:-JSON contains '$value'}"
    if echo "$json" | jq -e ". | tostring | contains(\"$value\")" > /dev/null 2>&1; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        return 1
    fi
}

assert_json_count() {
    local json="$1"
    local path="$2"
    local min_count="$3"
    local message="${4:-JSON has at least $min_count elements}"
    local actual=$(echo "$json" | jq "$path | length")
    if [[ "$actual" -ge "$min_count" ]]; then
        log_assertion "PASS" "$message ($actual >= $min_count)"
        return 0
    else
        log_assertion "FAIL" "$message ($actual < $min_count)"
        return 1
    fi
}
```

**Test Runner with --live flag:**
```bash
./run-tests.sh --live                    # Use live .env config
./run-tests.sh --live SYS-001            # Single test with live config
./run-tests.sh --live '*' downloads      # All download tests
```

The `--live` flag loads configuration from the project's `.env` file by parsing specific variables (avoids issues with unquoted values containing spaces):
```bash
ENV_FILE="/path/to/project/.env"
export TELEGRAM_BOT_TOKEN=$(grep '^TELEGRAM_BOT_TOKEN=' "$ENV_FILE" | cut -d'=' -f2-)
QBIT_HOST=$(grep '^QBIT_HOST=' "$ENV_FILE" | cut -d'=' -f2-)
QBIT_PORT=$(grep '^QBIT_PORT=' "$ENV_FILE" | cut -d'=' -f2-)
export QBITTORRENT_URL="http://${QBIT_HOST}:${QBIT_PORT}"
```

**CLI Test Results (working):**
- ✅ SYS-001-help-cli.sh - capabilities list (0.22s)
- ✅ SYS-002-health-cli.sh - bot.ping (0.18s)
- ✅ DL-001-downloads-cli.sh - download list (1.19s)
- ⚠️ TOR-001-search-cli.sh - torrent search (depends on Jackett connectivity)
- ⚠️ NL-001-natural-language-cli.sh - LLM NL (depends on model quality)

### ✅ Test HTTP Endpoint (IMPLEMENTED) — Full Stack E2E Testing

**The Solution:** Added ASP.NET minimal API test endpoint to the Telegram bot that accepts synthetic `Update` JSON and returns the bot's response.

**Implementation in Program.cs:**
```csharp
// Start test HTTP endpoint for E2E testing (runs alongside polling)
var testEndpointTask = Task.Run(async () =>
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls("http://0.0.0.0:5000");
    var app = builder.Build();
    
    app.MapPost("/test/inject-update", async (HttpContext context) =>
    {
        var updateJson = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var update = JsonSerializer.Deserialize<Update>(updateJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        // Capture response using RecordingMessenger
        var recordingMessenger = new RecordingTelegramMessenger();
        var testAdapter = new TelegramProductionAdapter(engine, recordingMessenger);
        
        await testAdapter.HandleUpdateAsync(update, cts.Token);
        
        // Extract response text (prefer edited messages over sent progress)
        var responseText = recordingMessenger.Edited.Count > 0 
            ? recordingMessenger.Edited.Last().Text 
            : recordingMessenger.Sent.Count > 0
                ? recordingMessenger.Sent.Last().Text 
                : "No response";
        
        return Results.Ok(new { 
            success = true, 
            response = responseText,
            messagesSent = recordingMessenger.Sent.Count,
            messagesEdited = recordingMessenger.Edited.Count,
            allSent = recordingMessenger.Sent.Select(m => m.Text).ToList(),
            allEdited = recordingMessenger.Edited.Select(m => m.Text).ToList()
        });
    });
    
    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    await app.RunAsync(cts.Token);
}, cts.Token);
```

**docker-compose.yaml port mapping:**
```yaml
homelynx-bot:
  ports:
    - "5000:5000"
```

**Usage from bash tests:**
```bash
# helpers/telegram.sh
send_telegram_command() {
    local command="$1"
    local chat_id="${TEST_CHAT_ID:-8153696940}"
    
    local update_json=$(cat <<EOF
{
    "update_id": $(date +%s),
    "message": {
        "message_id": $(shuf -i 1000-9999 -n 1),
        "date": $(date +%s),
        "chat": {"id": $chat_id, "type": "private"},
        "from": {"id": $chat_id, "is_bot": false, "first_name": "E2E Test"},
        "text": "$command"
    }
}
EOF
)
    
    local response=$(curl -s -X POST "http://localhost:5000/test/inject-update" \
        -H "Content-Type: application/json" \
        -d "$update_json" --max-time 60)
    
    echo "$response" | jq -r '.response'
}
```

**Example test:**
```bash
curl -s -X POST http://localhost:5000/test/inject-update \
  -H "Content-Type: application/json" \
  -d '{
    "update_id": 1,
    "message": {
      "message_id": 100,
      "chat": {"id": 8153696940, "type": "private"},
      "from": {"id": 8153696940, "is_bot": false, "first_name": "Test"},
      "text": "/health"
    }
  }' | jq .
```

**Response:**
```json
{
  "success": true,
  "response": "Engine is healthy",
  "messagesSent": 1,
  "messagesEdited": 3,
  "allSent": ["Working..."],
  "allEdited": ["parse: received update", "plan: submitting to orchestrator", "Engine is healthy"]
}
```

**Key Design Decisions:**

1. **RecordingTelegramMessenger** — Captures all outbound messages (sent + edited). Final response is the last edited message.

2. **Progress visibility** — Response includes all sent/edited messages so tests can verify progress reporting.

3. **Runs alongside polling** — Test endpoint runs in parallel with normal Telegram polling. No conflicts.

4. **Port 5000** — Exposed via docker-compose for host access.

5. **No authentication** — Test endpoint is open (acceptable for local dev/testing).

**Comparison: CLI vs Test Endpoint Testing:**

| Test Path | Speed | Coverage | Use Case |
|-----------|-------|----------|----------|
| **CLI** | ⚡ Fast (0.2s) | Engine + capabilities | Unit/integration tests, CI/CD |
| **Test Endpoint** | ⚡ Fast (1-2s) | Full stack (Telegram → Engine) | E2E tests, smoke tests |

**Recommended Strategy:**
- Use **CLI** for fast feedback during development
- Use **Test Endpoint** for E2E validation in CI/CD

### ✅ Dual-Path Testing Implementation (ACHIEVED)

**Test Results:** 96% pass rate (26/27 tests passing)

**Implementation:**
```bash
# helpers/dual-path.sh
run_dual() {
    local test_func="$1"
    local test_id="$2"

    # CLI path
    CURRENT_ADAPTER="cli"
    log_step "=== Testing via CLI adapter ==="
    if ! $test_func "cli"; then
        log_error "CLI path failed"
        return 1
    fi
    log_success "CLI path passed"

    # Telegram path (via test endpoint)
    CURRENT_ADAPTER="telegram"
    log_step "=== Testing via Telegram adapter ==="
    if ! $test_func "telegram"; then
        log_error "Telegram path failed"
        return 1
    fi
    log_success "Telegram path passed"
}

# Adapter-agnostic command execution
bot_call() {
    local target="$1"
    shift

    case "$CURRENT_ADAPTER" in
        cli)
            cli_call "$target" "$@"
            ;;
        telegram)
            send_telegram_command "$target"
            ;;
    esac
}
```

**Example dual-path test:**
```bash
#!/bin/bash
# SYS-002-health-dual.sh

test_health() {
    local adapter="$1"

    if is_cli; then
        RESULT=$(cli_call "system.health" --json)
        assert_json_field "$RESULT" ".RawResult.Success" "true"
        STATUS=$(extract_cli_field "$RESULT" ".RawResult.CapabilityResult.Data.status")
        assert_equals "healthy" "$STATUS"

    elif is_telegram; then
        RESULT=$(send_telegram_command "/health")
        assert_contains "$RESULT" "healthy"
    fi
}

run_dual test_health "SYS-002"
```

**Working dual-path tests:**
- ✅ SYS-001-help-dual.sh - capabilities list
- ✅ SYS-002-health-dual.sh - health check
- ✅ DL-001-downloads-dual.sh - downloads list
- ✅ TOR-001-search-dual.sh - torrent search
- ✅ WF-001-download-workflow-telegram.sh - full download workflow (Telegram only)

**Benefits:**
1. **Single test, two adapters** - Write once, test both CLI and Telegram
2. **Adapter-agnostic assertions** - Same test logic works for both
3. **Fast feedback** - CLI tests run in 0.2s, Telegram in 1-2s
4. **Full coverage** - CLI tests engine, Telegram tests full stack

### Bug Fixes Applied

**1. ACL Selector Bug**
- **Problem:** User with "ALL" permissions couldn't access capabilities with "USER" permission
- **Fix:** Updated `AclMatcher.SelectorMatches()` to treat "ALL" as matching all permission levels
- **Code:** `src/TorrentBot.Acl/AclMatcher.cs`

**2. Provider Detection Bug**
- **Problem:** `DownloadStartHandler` always used "url" provider when URL parameter was present, even for Jackett torrent URLs
- **Fix:** Added `DetectProvider()` method that checks URL patterns (`/dl/`, `jackett`, `.torrent`) to identify torrent URLs
- **Code:** `src/TorrentBot.Plugins.Downloads/Capabilities/DownloadStartHandler.cs`

**3. CLI-Telegram State Isolation**
- **Problem:** CLI and bot have separate engine instances, don't share confirmation tokens or download jobs
- **Solution:** Created Telegram-only workflow test (`WF-001-download-workflow-telegram.sh`) that uses test endpoint
- **Code:** `e2e-tests/tests/workflow/WF-001-download-workflow-telegram.sh`

### Other Pitfalls

1. **Polling for responses** — Telegram Bot API doesn't push; you must poll `getUpdates` and match `reply_to_message.message_id` to correlate responses.

2. **Timing** — After sending a command that triggers a side effect (e.g., adding a torrent), wait 2-3 seconds before checking the external API.

3. **Cleanup** — Every test that creates state must clean up. Use `add_test_torrent` / `remove_test_torrent` pairs.

4. **Test isolation** — Each test should be runnable independently. Don't rely on state from previous tests.

5. **Config in env** — Keep tokens and URLs in `config.env`, not hardcoded in tests. Never commit real tokens.

6. **Bot not responding** — If tests hang waiting for responses, the bot may not be processing updates. Debug checklist:
   - Verify bot container is running: `docker ps | grep bot`
   - Check bot logs for errors: `docker logs <bot-container> 2>&1 | grep -i error`
   - Test Telegram API connectivity from host: `curl https://api.telegram.org/bot<TOKEN>/getMe`
   - Send manual test message via curl and check if bot responds:
     ```bash
     curl -X POST "https://api.telegram.org/bot<TOKEN>/sendMessage" \
       -H "Content-Type: application/json" \
       -d '{"chat_id": <CHAT_ID>, "text": "/ping"}'
     ```
   - If bot doesn't respond, check: network connectivity, allowed users config, bot initialization logs
   - Common issue: bot starts polling but can't reach Telegram API (DNS/network issue in container)

7. **getUpdates offset** — When polling for responses, use `offset=<last_update_id>` to avoid re-processing old updates. Get the latest update_id before sending your test command.

8. **Bot response matching** — The bot's response is a new message from the bot (not a reply). Match by checking `message.from.is_bot == true` and filtering out "Working..." progress messages.

9. **CLI: Use array-based command construction, NOT `eval` with string** — Long magnet URIs (600+ chars) with special characters (`&`, `?`, `+`, `%`) cause "Argument list too long" or shell interpretation errors when built as strings. Use bash arrays:
   ```bash
   # ❌ WRONG — breaks with long magnet URIs
   local cmd="$CLI_PATH capability call download.start"
   cmd="$cmd --param magnet='$MAGNET'"
   RESPONSE=$(eval "$cmd" 2>/dev/null)
   
   # ✅ CORRECT — bash arrays handle any argument length
   local -a cmd=("$CLI_PATH" "capability" "call" "download.start")
   cmd+=("--param" "magnet=$MAGNET")
   cmd+=("--json")
   RESPONSE=$("${cmd[@]}" 2>/dev/null)
   ```

10. **CLI: Docker service names need localhost mapping** — `.env` has Docker-internal hostnames (`QBIT_HOST=qbittorrent`, `JACKETT_HOST=jackett`). CLI runs on the host, so these must be mapped to `localhost`:
    ```bash
    # In helpers/cli.sh — after sourcing .env
    if [[ -z "$QBITTORRENT_URL" && -n "$QBIT_HOST" ]]; then
        export QBITTORRENT_URL="http://localhost:${QBIT_PORT:-8080}"
    fi
    if [[ -z "$JACKETT_URL" && -n "$JACKETT_HOST" ]]; then
        export JACKETT_URL="http://localhost:${JACKETT_PORT:-9117}"
    fi
    # Also map username/password env var names
    [[ -n "$QBIT_USERNAME" ]] && export QBITTORRENT_USER="${QBITTORRENT_USER:-$QBIT_USERNAME}"
    [[ -n "$QBIT_PASSWORD" ]] && export QBITTORRENT_PASS="${QBITTORRENT_PASS:-$QBIT_PASSWORD}"
    ```

11. **CLI: Search results don't persist between invocations** — `TorrentDownloader._lastSearchResults` is in-memory. Each CLI call creates a new engine instance, so `index=N` from a previous search won't work. Always pass the full `magnet` URI instead:
    ```bash
    # ❌ WRONG — index lost between CLI calls
    SEARCH=$(cli_call download.search --param query=ubuntu)
    START=$(cli_call download.start --param index=0)  # "searchIndex not found"
    
    # ✅ CORRECT — extract magnet from search, pass directly
    SEARCH=$(cli_call download.search --param query=ubuntu)
    MAGNET=$(echo "$SEARCH" | jq -r '.RawResult.CapabilityResult.Data.results[0].magnet')
    START=$(cli_call download.start --param magnet="$MAGNET")
    ```

12. **CLI: Workflow tests need qBittorrent cleanup** — qBittorrent returns "Conflict" if you try to add a torrent that already exists. Workflow tests must clean up qBittorrent state before running:
    ```bash
    # Cleanup qBittorrent before workflow test
    COOKIE_FILE="/tmp/qbit-cookies-$$"
    curl -s -c "$COOKIE_FILE" -X POST "$QBIT_URL/api/v2/auth/login" \
      -d "username=$QBIT_USER&password=$QBIT_PASS" >/dev/null
    HASHES=$(curl -s -b "$COOKIE_FILE" "$QBIT_URL/api/v2/torrents/info" | jq -r '.[].hash')
    for hash in $HASHES; do
        curl -s -b "$COOKIE_FILE" -X POST "$QBIT_URL/api/v2/torrents/delete" \
          -d "hashes=$hash&deleteFiles=false" >/dev/null
    done
    rm -f "$COOKIE_FILE"
    ```

13. **CLI: Confirmation flow requires FileBasedConfirmationStore** — The default `ConfirmationStore` is in-memory and tokens don't survive between CLI process invocations. CLI must use `FileBasedConfirmationStore` which persists tokens to `/tmp/homelynx-confirmations.json`. See the `cli-confirmation-persistence` skill for implementation details.

### ✅ Dual-Path Testing (CLI + Telegram via Userbot)

**The Pattern:** Write one test function that runs against both CLI and Telegram adapters, ensuring parity between execution paths.

**Why This Matters:**
- CLI tests are fast (0.2s) but don't test Telegram adapter
- Telegram tests are slow (5-15s) but test full stack
- Dual-path ensures both adapters produce equivalent results
- Catches adapter-specific bugs early

**Framework (`helpers/dual-path.sh`):**

```bash
#!/bin/bash
# Dual-path E2E test framework
source "$SCRIPT_DIR/common.sh"
source "$SCRIPT_DIR/cli.sh"
source "$SCRIPT_DIR/userbot.sh"
source "$SCRIPT_DIR/assertions.sh"

# Current adapter being tested
CURRENT_ADAPTER=""
TARGET_BOT="${TARGET_BOT:-@Media_bot}"

# Run test function for both adapters
run_dual() {
    local test_func="$1"
    local test_id="$2"
    
    # CLI path
    CURRENT_ADAPTER="cli"
    log_step "=== Testing via CLI adapter ==="
    if ! $test_func "cli"; then
        [ -n "$test_id" ] && exit_test "$test_id" "FAIL" "CLI adapter test failed"
        return 1
    fi
    
    # Telegram path (via userbot)
    if userbot_check_auth; then
        CURRENT_ADAPTER="telegram"
        log_step "=== Testing via Telegram adapter (userbot → $TARGET_BOT) ==="
        if ! $test_func "telegram"; then
            [ -n "$test_id" ] && exit_test "$test_id" "FAIL" "Telegram adapter test failed"
            return 1
        fi
    else
        log_warning "Userbot not authenticated, skipping Telegram path"
    fi
    
    return 0
}

# Helper functions
is_cli() { [ "$CURRENT_ADAPTER" = "cli" ]; }
is_telegram() { [ "$CURRENT_ADAPTER" = "telegram" ]; }

# Extract data from CLI JSON response
extract_cli_field() {
    local json="$1"
    local path="$2"
    echo "$json" | jq -r "$path" 2>/dev/null
}
```

**Dual-Path Test Template:**

```bash
#!/bin/bash
# SYS-002: Health Check — Dual Path Test (CLI + Telegram)

source "$(dirname "$0")/../../helpers/dual-path.sh"

TEST_ID="SYS-002"
TEST_NAME="Health Check (Dual Path)"

log_test_start "$TEST_ID" "$TEST_NAME"
start_timer

# Test function — runs for each adapter
test_health() {
    local adapter="$1"
    
    if is_cli; then
        # CLI path — call capability directly
        RESULT=$(cli_call "system.health" --json)
        
        assert_json_valid "$RESULT" "Health response is valid JSON" || return 1
        assert_json_field "$RESULT" ".RawResult.Success" "true" "Health check succeeded" || return 1
        
        STATUS=$(extract_cli_field "$RESULT" ".RawResult.CapabilityResult.Data.status")
        assert_equals "healthy" "$STATUS" "System status is healthy" || return 1
        
    elif is_telegram; then
        # Telegram path — send command via userbot
        RESULT=$(userbot_send "$TARGET_BOT" "/health")
        
        assert_not_empty "$RESULT" "Received response from bot" || return 1
        assert_contains "$RESULT" "healthy" "Response contains 'healthy'" || return 1
    fi
    
    return 0
}

# Run dual-path test
run_dual test_health "$TEST_ID"
TEST_RESULT=$?

stop_timer

if [ $TEST_RESULT -eq 0 ]; then
    exit_test "$TEST_ID" "PASS" "Health check passed on all adapters (${TEST_DURATION}s)"
else
    exit_test "$TEST_ID" "FAIL" "Health check failed on one or more adapters"
fi
```

**Userbot Bash Wrapper (`helpers/userbot.sh`):**

```bash
#!/bin/bash
# Userbot helper wrapper for bash E2E tests

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
USERBOT_SCRIPT="$SCRIPT_DIR/userbot_helper.py"

# Send message to bot and get response
userbot_send() {
    local bot="$1"
    local message="$2"
    local timeout="${3:-30}"
    
    python3 "$USERBOT_SCRIPT" send "$bot" "$message" --timeout "$timeout" 2>/dev/null
}

# Check if userbot is authenticated
userbot_check_auth() {
    [ -f "$SCRIPT_DIR/e2e_test_session.session" ]
}

export -f userbot_send userbot_check_auth
```

**Key Design Decisions:**

1. **Single test function, two adapters** — The `test_health()` function checks `is_cli` or `is_telegram` and runs adapter-specific code. This keeps test logic in one place.

2. **Graceful degradation** — If userbot is not authenticated, Telegram path is skipped with a warning. CLI tests still run.

3. **Adapter-agnostic assertions** — Both paths verify the same outcome (e.g., "healthy" status), but extract data differently:
   - CLI: Parse JSON with `jq`
   - Telegram: Search text with `grep`/`contains`

4. **Named test files** — Use `-dual` suffix to indicate dual-path tests: `SYS-002-health-dual.sh`

**When to Use Dual-Path:**

| Scenario | Use Dual-Path? | Why |
|----------|----------------|-----|
| Basic commands (/help, /health) | ✅ Yes | Quick parity check |
| Download workflows | ✅ Yes | Verify CLI and Telegram produce same results |
| Natural language | ✅ Yes | Test LLM pipeline via both adapters |
| Error handling | ✅ Yes | Ensure errors are consistent |
| Performance tests | ❌ No | CLI is always faster, not comparable |
| UI-specific features (inline buttons) | ❌ No | CLI doesn't have UI |

**Comparison: Test Approaches:**

| Approach | Speed | Coverage | Automation | Use Case |
|----------|-------|----------|------------|----------|
| **CLI only** | ⚡ 0.2s | Engine + capabilities | ✅ Full | Development, CI/CD |
| **Userbot only** | 🐢 5-15s | Full stack | ✅ Full | E2E validation |
| **Dual-path** | 🐢 5-15s | Both paths | ✅ Full | Parity verification |
| **Manual Telegram** | 🐢 5-15s | Full stack | ❌ Manual | Ad-hoc testing |

**Recommended Strategy:**
- **Development:** CLI tests for fast feedback
- **Pre-deployment:** Dual-path tests for parity verification
- **Release candidates:** Full userbot E2E suite
- **Production monitoring:** Manual testing via real Telegram client

## When to Use

- Telegram bot with external service integrations (torrent clients, media servers, etc.)
- Need to verify both bot responses AND system state changes
- Multi-language NL support needs testing
- Want reproducible, scriptable test execution with HTML reports
- **Need to ensure CLI and Telegram adapters produce equivalent results (dual-path testing)**
