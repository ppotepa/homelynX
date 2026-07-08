# E2E Test Plan — Homelynx (Basic Only)

## Test Status Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Pass |
| ❌ | Fail |
| ⏳ | Pending |
| 🚧 | In Progress |

---

## Environment Setup

### Prerequisites for All Tests

1. **Docker containers running**:
   ```bash
   docker compose up -d
   ```
   Required services:
   - `homelynx-bot` - main bot service
   - `llm` - Ollama LLM service (qwen3:0.6b model)
   - `jackett` - torrent indexer (15+ indexers configured)
   - `qbittorrent` - torrent client
   - (removed) surveillance service (no longer present)

2. **Telegram bot configured**:
   - Bot token set in `.env` (TELEGRAM_BOT_TOKEN)
   - Bot is running and responsive
   - Test user has permission to send commands

3. **Test user setup**:
   - User ID: `8153696940` (or your test user)
   - User has `ALL` permissions in `allowed-users.cfg`
   - Format: `8153696940 ALL`

4. **Verbosity level**:
   - Set to `full` for detailed output: send `verbosity full` to bot
   - This shows plan, execution, and response details

5. **Test data preparation**:
   - Media directory exists: `/home/ppotepa/mediaserver/`
   - At least one test file > 1GB: `/home/ppotepa/mediaserver/test-large-file.iso`
   - Jackett indexers are configured and responding
   - qBittorrent is accessible at `http://qbittorrent:8080`

6. **Network connectivity**:
   - Internet access for torrent search
   - Access to test URLs: `https://releases.ubuntu.com/`
   - Access to magnet links

---

## Test Execution Format

Each test includes:
- **Prerequisites**: What must be true before running the test
- **Steps**: Exact actions to perform (1, 2, 3...)
- **Expected Output**: What should happen (specific format)
- **Verification**: How to confirm the test passed (checklist)
- **Cleanup**: What to clean up after the test (if needed)

---

## Test Categories

### Category 1: Basic Command Tests
Simple slash commands and NL queries that return information.

### Category 2: State Verification Tests
Tests that verify system state changes after actions.

### Category 3: Full Workflow Tests
Multi-step scenarios that complete a full task.

### Category 4: Context Persistence Tests
Tests that verify conversation context is maintained.

### Category 5: Error Handling & Recovery Tests
Tests for error conditions and recovery.

### Category 6: Multi-Language Tests
Tests in Polish, English, and mixed languages.

### Category 7: Performance Tests
Tests for response times and timeouts.

### Category 8: Data Integrity Tests
Tests that verify data correctness.

### Category 9: Edge Cases
Tests for unusual inputs and boundary conditions.

### Category 10: Permission & ACL Tests
Tests for access control.

### Category 11: Real Data Tests
Tests with actual downloads and real files.

---

## Surveillance Focus (Separate)

(See SURVEILLANCE_SCENARIOS.md was removed along with the surveillance feature. All scenarios now in COMPLEX_MULTI_STAGE_SCENARIOS.md targeting torrent/download/system flows.)

**Important**: For surveillance testing we completely de-scope:
- Coordinate input (coords) features and bots
- Torrent / download features (unless explicitly cross-referenced)
- Other non-surveillance modules

Surveillance scenarios are self-contained around events, incidents, media (clips/previews/snapshots/transcripts), LLM summaries, live views, stats, etc.

### Category 12: Idempotency Tests
Tests that verify repeated actions are handled correctly.

---

## SYSTEM DOMAIN

### Basic System Commands

#### SYS-001: `/help`

**Prerequisites:**
- Bot is running and responsive
- Test user is authenticated

**Steps:**
1. Open Telegram chat with the bot
2. Send command: `/help`

**Expected Output:**
Lista 50+ komend pogrupowana po modułach:
```
BOT
  /diag                  Show diagnostic status
  /ping                  Responds with pong
  /plugins               Show hot plugin status

DOWNLOAD
  /cancel                Cancel and remove a download
  /download              Start a download from torrent or URL
  /downloads             Lists active and recent downloads
  /download_search       Search for downloadable content
  ...

JOBS
  /jobs                  List tracked engine jobs
  /job_cancel            Cancel a tracked engine job

MEDIA
  /media                 List known media files in the library

SYSTEM
  /capabilities          Lists capabilities available to current user
  /disk_usage            Show disk usage for the media root drive
  /find_large_files      Find large files under the media root
  /health                Returns basic engine health
  /help                  Show available commands for the current user
  ...

TORRENT
  /cancel_search         Cancel the active torrent search session
  /download_candidate    Search and auto-start the best torrent
  /more                  Show next page of torrent search results
  /search                Search torrent indexers via Jackett
  /select                Select a numbered torrent search result
  /torrents              List torrents managed by qBittorrent
  ...
```

**Verification:**
- ✅ Response contains all module sections (BOT, DOWNLOAD, JOBS, MEDIA, SYSTEM, TORRENT)
- ✅ Each command has a description
- ✅ Command count matches expected (~50+ commands)
- ✅ Response is formatted with proper indentation

---

#### SYS-002: `/health`

**Prerequisites:**
- Bot is running
- Engine is healthy (no critical errors)

**Steps:**
1. Send command: `/health`

**Expected Output:**
```
Engine is healthy
```

**Verification:**
- ✅ Response contains "Engine is healthy" or similar success message
- ✅ No error messages in response
- ✅ Response time < 2 seconds

---

#### SYS-003: `/status`

**Prerequisites:**
- Bot is running
- All plugins are loaded

**Steps:**
1. Send command: `/status`

**Expected Output:**
```
Runtime status:
  Uptime: 2h 15m
  Plugins loaded: 8
  Memory usage: 256MB
  Active connections: 1
```

**Verification:**
- ✅ Response shows uptime
- ✅ Response shows plugin count (should be 8)
- ✅ Response shows memory usage
- ✅ All values are reasonable (no negative numbers, no nulls)

---

#### SYS-004: `/ping`

**Prerequisites:**
- Bot is running

**Steps:**
1. Send command: `/ping`

**Expected Output:**
```
pong
```

**Verification:**
- ✅ Response is exactly "pong" (case-insensitive)
- ✅ Response time < 1 second

---

#### SYS-005: `/capabilities`

**Prerequisites:**
- Bot is running
- Test user has permissions

**Steps:**
1. Send command: `/capabilities`

**Expected Output:**
```
Capabilities available to user 8153696940:
  system.help (PUBLIC, Safe)
  system.health (PUBLIC, Safe)
  torrent.search (USER, Safe)
  downloads.list (USER, Safe)
  ...
```

**Verification:**
- ✅ Response lists capabilities
- ✅ Each capability shows permission level (PUBLIC/USER)
- ✅ Each capability shows risk level (Safe/Destructive)
- ✅ No capabilities with ADMIN permission shown (unless user is admin)

---

#### SYS-006: `/llm_status`

**Prerequisites:**
- Bot is running
- Ollama service is running

**Steps:**
1. Send command: `/llm_status`

**Expected Output:**
```
LLM pipeline mode: ollama
Planner model: qwen3:0.6b
Executor model: qwen3:0.6b
Responder model: qwen3:0.6b
```

**Verification:**
- ✅ Response shows mode is "ollama" (not "stub")
- ✅ All three models are shown
- ✅ Model names match configuration in `.env`

---

#### SYS-007: `/disk_usage`

**Prerequisites:**
- Bot is running
- Media root directory exists

**Steps:**
1. Send command: `/disk_usage`

**Expected Output:**
```
Disk usage for /:
  Total: 500.0 GB
  Free: 200.0 GB
  Used: 300.0 GB (60%)
```

**Verification:**
- ✅ Response shows disk path
- ✅ Total, Free, Used values are shown
- ✅ Values are reasonable (Total > 0, Free >= 0, Used >= 0)
- ✅ Total ≈ Free + Used (within 1% tolerance)

---

#### SYS-008: `/find_large_files`

**Prerequisites:**
- Bot is running
- Media root directory exists
- At least one file > 1GB exists in media root

**Steps:**
1. Send command: `/find_large_files`

**Expected Output:**
```
Found 5 large file(s) (>1GB):
  /media/movies/Inception.mkv - 4.2 GB
  /media/movies/The.Matrix.mkv - 3.8 GB
  /media/tv/Breaking.Bad.S01E01.mkv - 1.5 GB
  ...
```

**Verification:**
- ✅ Response shows file count
- ✅ Each file shows path and size
- ✅ All files are > 1GB
- ✅ Files are sorted by size (descending)

---

#### SYS-009: `/find_large_files 500`

**Prerequisites:**
- Bot is running
- Media root directory exists
- At least one file > 500MB exists

**Steps:**
1. Send command: `/find_large_files 500`

**Expected Output:**
```
Found 10 large file(s) (>500MB):
  /media/movies/Inception.mkv - 4.2 GB
  /media/music/Pink.Floyd.flac - 800 MB
  ...
```

**Verification:**
- ✅ Response shows files > 500MB (not 1GB)
- ✅ Parameter is correctly parsed
- ✅ More files shown than default (1GB threshold)

---

#### SYS-010: `/diag`

**Prerequisites:**
- Bot is running
- All services are configured

**Steps:**
1. Send command: `/diag`

**Expected Output:**
```
Diagnostic status:
  Ollama: OK (connected, model: qwen3:0.6b)
  Jackett: OK (15 indexers configured)
  qBittorrent: OK (3 active torrents)
  Surveillance: OK (recording)
  TTS: OK (pl_PL voice loaded)
```

**Verification:**
- ✅ All services are checked
- ✅ Each service shows status (OK/ERROR)
- ✅ Details are shown for each service
- ✅ No service shows ERROR (unless intentionally misconfigured)

---

#### SYS-011: `/plugins`

**Prerequisites:**
- Bot is running
- plugins/hot/ directory exists (can be empty)

**Steps:**
1. Send command: `/plugins`

**Expected Output:**
```
Hot plugins: 0 loaded
```
OR (if plugins exist):
```
Hot plugins: 2 loaded
  - custom_plugin v1.0.0
  - another_plugin v2.1.0
```

**Verification:**
- ✅ Response shows plugin count
- ✅ If plugins exist, names and versions are shown
- ✅ Response matches actual contents of plugins/hot/ directory

---

### Natural Language - System

#### SYS-012: "what commands are available"

**Prerequisites:**
- Bot is running
- LLM is configured and working

**Steps:**
1. Send message: `what commands are available`

**Expected Output:**
Same as `/help` - lista komend pogrupowana po modułach

**Verification:**
- ✅ LLM correctly maps NL to system.help capability
- ✅ Response is same format as /help command
- ✅ All modules are shown

---

#### SYS-013: "is the bot alive"

**Prerequisites:**
- Bot is running
- LLM is configured

**Steps:**
1. Send message: `is the bot alive`

**Expected Output:**
```
Engine is healthy
Uptime: 2h 15m
```

**Verification:**
- ✅ LLM maps to system.health
- ✅ Response shows health status
- ✅ Response is informative (not just "yes")

---

#### SYS-014: "show system status"

**Prerequisites:**
- Bot is running
- LLM is configured

**Steps:**
1. Send message: `show system status`

**Expected Output:**
Same as `/status` - runtime info + plugins

**Verification:**
- ✅ LLM maps to system.status
- ✅ Response shows uptime, plugins, memory

---

#### SYS-015: "how much disk space is left"

**Prerequisites:**
- Bot is running
- LLM is configured

**Steps:**
1. Send message: `how much disk space is left`

**Expected Output:**
```
Free: 200.0 GB
Total: 500.0 GB
Used: 300.0 GB (60%)
```

**Verification:**
- ✅ LLM maps to system.disk_usage
- ✅ Response shows disk space info
- ✅ Values are clear and readable

---

#### SYS-016: "find large files over 2GB"

**Prerequisites:**
- Bot is running
- LLM is configured
- Files > 2GB exist

**Steps:**
1. Send message: `find large files over 2GB`

**Expected Output:**
```
Found 3 large file(s) (>2GB):
  /media/movies/Inception.mkv - 4.2 GB
  /media/movies/The.Matrix.mkv - 3.8 GB
  ...
```

**Verification:**
- ✅ LLM extracts parameter (2GB) from NL
- ✅ Only files > 2GB are shown
- ✅ Parameter is correctly passed to capability

---

#### SYS-017: "what can you do"

**Prerequisites:**
- Bot is running
- LLM is configured

**Steps:**
1. Send message: `what can you do`

**Expected Output:**
Lista capabilities (jak /capabilities)

**Verification:**
- ✅ LLM maps to system.capabilities
- ✅ Response lists available capabilities

---

#### SYS-018: "show me the help"

**Prerequisites:**
- Bot is running
- LLM is configured

**Steps:**
1. Send message: `show me the help`

**Expected Output:**
Lista komend (jak /help)

**Verification:**
- ✅ LLM maps to system.help
- ✅ Response is same as /help command

---

#### SYS-019: "are you working"

**Prerequisites:**
- Bot is running
- LLM is configured

**Steps:**
1. Send message: `are you working`

**Expected Output:**
```
Engine is healthy
```

**Verification:**
- ✅ LLM maps to system.health
- ✅ Response confirms bot is working

---

#### SYS-020: "diagnostics"

**Prerequisites:**
- Bot is running
- LLM is configured

**Steps:**
1. Send message: `diagnostics`

**Expected Output:**
Diagnostic status (jak /diag)

**Verification:**
- ✅ LLM maps to /diag capability
- ✅ All services are checked

---

## DOWNLOADS DOMAIN

### Category 1: Basic Command Tests

#### DL-001: `/downloads`

**Prerequisites:**
- Bot is running
- qBittorrent has at least 1 active torrent
- Test user has permissions

**Steps:**
1. Send command: `/downloads`

**Expected Output:**
```
Active downloads (2):
  1) ubuntu-24.04-desktop.iso
     Status: downloading
     Progress: 45% (2.1GB / 4.7GB)
     Speed: 2.5 MB/s
     ETA: 15 minutes
  
  2) fedora-workstation-39.iso
     Status: completed
     Size: 3.2GB
     Seeding: Yes (ratio 1.5)
```

**Verification:**
- ✅ Response shows active downloads count
- ✅ Each download shows name, status, progress
- ✅ Active downloads show speed and ETA
- ✅ Completed downloads show size and seeding status
- ✅ Response time < 3 seconds

**Cleanup:** None (read-only command)

---

#### DL-002: `/download_search acdc`

**Prerequisites:**
- Bot is running
- Jackett is configured with indexers
- Internet connectivity

**Steps:**
1. Send command: `/download_search acdc`

**Expected Output:**
```
Search results for "acdc" (10 of 150):

  1) ACDC - Back in Black (1980) [FLAC]
     Size: 1.2GB | Seeds: 150 | Peers: 25
     
  2) ACDC - Highway to Hell (1979) [MP3 320kbps]
     Size: 900MB | Seeds: 120 | Peers: 18
     
  3) ACDC - The Razors Edge (1990) [FLAC]
     Size: 1.1GB | Seeds: 95 | Peers: 12
  ...
  
Use /select <number> to download
Use /more for next page
```

**Verification:**
- ✅ Response shows search query
- ✅ Results show name, size, seeds, peers
- ✅ At least 5 results shown
- ✅ Results are sorted by seeds (descending)
- ✅ Response includes usage instructions
- ✅ Response time < 5 seconds

**Cleanup:** Send `/cancel_search` to clear session

---

#### DL-003: `/download_search ubuntu`

**Prerequisites:**
- Bot is running
- Jackett is configured
- Internet connectivity

**Steps:**
1. Send command: `/download_search ubuntu`

**Expected Output:**
```
Search results for "ubuntu" (10 of 500):

  1) ubuntu-24.04-desktop-amd64.iso
     Size: 4.7GB | Seeds: 500 | Peers: 120
     
  2) ubuntu-22.04.4-desktop-amd64.iso
     Size: 4.5GB | Seeds: 450 | Peers: 95
     
  3) ubuntu-24.04-server-amd64.iso
     Size: 2.6GB | Seeds: 380 | Peers: 75
  ...
```

**Verification:**
- ✅ Results show Ubuntu ISOs
- ✅ Sizes are realistic (2-5GB for desktop, 1-3GB for server)
- ✅ Seeds are > 0
- ✅ Response time < 5 seconds

**Cleanup:** Send `/cancel_search`

---

#### DL-004: `/torrents`

**Prerequisites:**
- Bot is running
- qBittorrent has torrents

**Steps:**
1. Send command: `/torrents`

**Expected Output:**
```
qBittorrent torrents (3):

  1) ubuntu-24.04-desktop.iso
     Status: downloading
     Progress: 45% (2.1GB / 4.7GB)
     Speed: ↓ 2.5 MB/s | ↑ 0.5 MB/s
     Ratio: 0.0
     
  2) fedora-workstation-39.iso
     Status: seeding
     Size: 3.2GB (100%)
     Speed: ↓ 0 MB/s | ↑ 1.2 MB/s
     Ratio: 1.5
     
  3) debian-12.5.0-amd64-netinst.iso
     Status: paused
     Size: 600MB (0%)
     Speed: ↓ 0 MB/s | ↑ 0 MB/s
```

**Verification:**
- ✅ Response shows torrent count
- ✅ Each torrent shows name, status, progress
- ✅ Active torrents show download/upload speed
- ✅ Seeding torrents show ratio
- ✅ Response time < 3 seconds

**Cleanup:** None

---

### Category 2: State Verification Tests

#### DL-005: `/download url=...` - Verify torrent added to qBittorrent

**Prerequisites:**
- Bot is running
- qBittorrent is accessible
- No active download with same name

**Steps:**
1. Send command: `/download url=https://releases.ubuntu.com/24.04/ubuntu-24.04-desktop.iso`
2. Wait 2 seconds
3. Check qBittorrent API: `curl http://qbittorrent:8080/api/v2/torrents/info`

**Expected Output:**
Bot response:
```
Download started:
  Name: ubuntu-24.04-desktop.iso
  Size: 4.7GB
  Source: URL download
  
Monitoring progress...
```

qBittorrent API response:
```json
[
  {
    "name": "ubuntu-24.04-desktop.iso",
    "size": 5046583296,
    "progress": 0.0,
    "state": "downloading"
  }
]
```

**Verification:**
- ✅ Bot confirms download started
- ✅ Torrent appears in qBittorrent within 5 seconds
- ✅ Torrent name matches
- ✅ Torrent size matches (within 1% tolerance)
- ✅ Torrent state is "downloading"

**Cleanup:** Send `/cancel ubuntu-24.04-desktop.iso`

---

#### DL-009: `/pause ubuntu.iso` - Verify torrent paused

**Prerequisites:**
- Active torrent "ubuntu.iso" exists in qBittorrent
- Torrent is currently downloading

**Steps:**
1. Send command: `/pause ubuntu.iso`
2. Wait 2 seconds
3. Check qBittorrent API: `curl http://qbittorrent:8080/api/v2/torrents/info`

**Expected Output:**
Bot response:
```
Paused: ubuntu.iso
Progress: 45% (2.1GB / 4.7GB)
```

qBittorrent API response:
```json
[
  {
    "name": "ubuntu.iso",
    "state": "pausedDL",
    "progress": 0.45
  }
]
```

**Verification:**
- ✅ Bot confirms pause
- ✅ Torrent state changes to "pausedDL" within 3 seconds
- ✅ Progress is preserved
- ✅ Download speed becomes 0

**Cleanup:** Send `/resume ubuntu.iso`

---

#### DL-010: `/resume ubuntu.iso` - Verify torrent resumed

**Prerequisites:**
- Paused torrent "ubuntu.iso" exists in qBittorrent

**Steps:**
1. Send command: `/resume ubuntu.iso`
2. Wait 2 seconds
3. Check qBittorrent API

**Expected Output:**
Bot response:
```
Resumed: ubuntu.iso
Progress: 45% (2.1GB / 4.7GB)
Speed: ↓ 2.5 MB/s
```

qBittorrent API response:
```json
[
  {
    "name": "ubuntu.iso",
    "state": "downloading",
    "progress": 0.45,
    "dlspeed": 2621440
  }
]
```

**Verification:**
- ✅ Bot confirms resume
- ✅ Torrent state changes to "downloading" within 3 seconds
- ✅ Download speed > 0
- ✅ Progress continues from where it was paused

**Cleanup:** None

---

#### DL-011: `/cancel ubuntu.iso` - Verify torrent removed

**Prerequisites:**
- Torrent "ubuntu.iso" exists in qBittorrent

**Steps:**
1. Send command: `/cancel ubuntu.iso`
2. Wait 2 seconds
3. Check qBittorrent API

**Expected Output:**
Bot response:
```
Cancelled: ubuntu.iso
Torrent removed from qBittorrent
Files kept on disk
```

qBittorrent API response:
```json
[]
```

**Verification:**
- ✅ Bot confirms cancellation
- ✅ Torrent disappears from qBittorrent within 3 seconds
- ✅ API returns empty list or torrent not found
- ✅ Files remain on disk (check with `ls /downloads/`)

**Cleanup:** None

---

### Category 3: Full Workflow Tests

#### DL-WORKFLOW-001: Search → Select → Download → Monitor

**Prerequisites:**
- Bot is running
- Jackett and qBittorrent are accessible
- No active downloads

**Steps:**
1. Send: `/download_search ubuntu`
2. Wait for results (5 seconds)
3. Send: `/select 1`
4. Wait 10 seconds
5. Send: `/downloads`
6. Verify torrent is downloading
7. Send: `/cancel ubuntu-24.04-desktop.iso`

**Expected Output:**
Step 1-2: Search results shown
Step 3: "Selected: ubuntu-24.04-desktop.iso, download started"
Step 5: Download appears in list with progress > 0%
Step 7: Torrent cancelled

**Verification:**
- ✅ Search returns results
- ✅ Select starts download
- ✅ Download appears in /downloads within 10 seconds
- ✅ Progress increases over time
- ✅ Cancel removes torrent

**Cleanup:** None (already cancelled in step 7)

---

#### DL-WORKFLOW-002: Download URL → Wait → Complete

**Prerequisites:**
- Bot is running
- Small test file URL available (< 100MB)

**Steps:**
1. Send: `/download url=https://cdimage.debian.org/debian-cd/current/amd64/iso-cd/debian-12.5.0-amd64-netinst.iso`
2. Wait 30 seconds
3. Send: `/downloads`
4. Verify progress > 0%
5. Wait for completion (or cancel after 1 minute)

**Expected Output:**
Step 1: Download started
Step 3: Download shows progress
Step 5: Either completed or cancelled

**Verification:**
- ✅ Download starts successfully
- ✅ Progress increases
- ✅ Eventually completes or can be cancelled

**Cleanup:** Cancel if not completed

---

### Category 4: Context Persistence Tests

#### DL-CTX-001: Sequential queries use context

**Prerequisites:**
- Bot is running
- At least 1 active download exists

**Steps:**
1. Send: "show downloads"
2. Wait for response
3. Send: "how many are active"
4. Wait for response
5. Send: "is ubuntu downloading"

**Expected Output:**
Step 1: List of downloads
Step 3: Count from context (not new query)
Step 5: Status from context

**Verification:**
- ✅ First query returns full list
- ✅ Second query returns count without re-querying
- ✅ Third query returns specific status from context
- ✅ All responses are consistent
- ✅ Response times decrease (context is faster)

**Cleanup:** None

---

### Category 5: Error Handling & Recovery Tests

#### DL-ERR-001: Invalid URL

**Prerequisites:**
- Bot is running

**Steps:**
1. Send: `/download url=not-a-valid-url`

**Expected Output:**
```
Error: Invalid URL format
Please provide a valid HTTP/HTTPS URL
```

**Verification:**
- ✅ Error message is clear
- ✅ No crash or exception
- ✅ Suggests correct format

**Cleanup:** None

---

#### DL-ERR-002: Select invalid index

**Prerequisites:**
- Active search session with 10 results

**Steps:**
1. Send: `/download_search ubuntu`
2. Wait for results
3. Send: `/select 999`

**Expected Output:**
```
Error: Invalid selection
Please select a number between 1 and 10
```

**Verification:**
- ✅ Error message shows valid range
- ✅ No crash
- ✅ Search session remains active

**Cleanup:** `/cancel_search`

---

#### DL-ERR-003: Cancel non-existent torrent

**Prerequisites:**
- Bot is running
- No torrent named "nonexistent.iso"

**Steps:**
1. Send: `/cancel nonexistent.iso`

**Expected Output:**
```
Error: Torrent not found
No torrent named "nonexistent.iso" is active
```

**Verification:**
- ✅ Error message is clear
- ✅ Suggests checking active torrents
- ✅ No crash

**Cleanup:** None

---

### Category 6: Multi-Language Tests

#### DL-LANG-001: Polish - "pokaż pobierania"

**Prerequisites:**
- Bot is running
- LLM is configured

**Steps:**
1. Send: "pokaż pobierania"

**Expected Output:**
Same as `/downloads` - list of active downloads

**Verification:**
- ✅ LLM correctly maps Polish to downloads.list
- ✅ Response is in English (system language)
- ✅ All downloads are shown

**Cleanup:** None

---

#### DL-LANG-002: Polish - "szukaj ubuntu"

**Prerequisites:**
- Bot is running
- LLM is configured

**Steps:**
1. Send: "szukaj ubuntu"

**Expected Output:**
Same as `/download_search ubuntu` - search results

**Verification:**
- ✅ LLM maps Polish "szukaj" to search
- ✅ Results are shown
- ✅ Query is "ubuntu"

**Cleanup:** `/cancel_search`

---

#### DL-LANG-003: Mixed - "pokaż active downloads"

**Prerequisites:**
- Bot is running
- LLM is configured

**Steps:**
1. Send: "pokaż active downloads"

**Expected Output:**
List of active downloads

**Verification:**
- ✅ LLM handles mixed language
- ✅ Response is correct

**Cleanup:** None

---

### Category 12: Idempotency Tests

#### DL-IDEM-001: Pause already paused torrent

**Prerequisites:**
- Paused torrent "ubuntu.iso" exists

**Steps:**
1. Send: `/pause ubuntu.iso`
2. Wait 2 seconds
3. Send: `/pause ubuntu.iso` again

**Expected Output:**
First call: "Paused: ubuntu.iso"
Second call: "Error: Torrent already paused"

**Verification:**
- ✅ First pause succeeds
- ✅ Second pause returns error
- ✅ Error message is clear
- ✅ No crash

**Cleanup:** Resume or cancel torrent

---

#### DL-IDEM-002: Resume non-paused torrent

**Prerequisites:**
- Downloading torrent "ubuntu.iso" exists (not paused)

**Steps:**
1. Send: `/resume ubuntu.iso`

**Expected Output:**
```
Error: Torrent is not paused
Current status: downloading
```

**Verification:**
- ✅ Error message is clear
- ✅ Shows current status
- ✅ No crash

**Cleanup:** None

---

## Summary

**Total Tests**: 79 Basic + 20 Advanced = 99 tests

**By Category:**
- Basic Commands: 30 tests
- State Verification: 10 tests
- Full Workflows: 5 tests
- Context Persistence: 5 tests
- Error Handling: 10 tests
- Multi-Language: 5 tests
- Performance: 5 tests
- Data Integrity: 5 tests
- Edge Cases: 10 tests
- Permission & ACL: 5 tests
- Real Data: 5 tests
- Idempotency: 4 tests

---

## Test Execution Log

| Date | Tester | Test ID | Result | Notes |
|------|--------|---------|--------|-------|
| | | | | |

---

## Next Steps

1. Run all Basic tests manually in Telegram
2. Document results in Test Execution Log
3. Fix any failing tests
4. Once all Basic tests pass, add Medium tests
5. Then add Advanced tests

---

## JOBS DOMAIN

### Basic Job Commands

| ID | Input | Expected Output | Notes | Status |
|----|-------|-----------------|-------|--------|
| JOB-001 | `/jobs` | Jobs: 1) download-abc123 (running, 45%, started 5min ago) 2) organize-def456 (queued) 3) download-ghi789 (completed, 100%) | Lista jobów | ⏳ |
| JOB-002 | `/job_cancel download-abc123` | Cancelled job: download-abc123 | Anulowanie joba | ⏳ |
| JOB-003 | `/job_cancel organize-def456` | Cancelled job: organize-def456 | Anulowanie joba | ⏳ |

### Natural Language - Jobs

| ID | Input | Expected Output | Notes | Status |
|----|-------|-----------------|-------|--------|
| JOB-004 | "show active jobs" | 1) download-abc123 (running, 45%) 2) organize-def456 (queued) | NL → /jobs | ⏳ |
| JOB-005 | "any pending tasks" | 1) organize-def456 (queued) | Filtrowanie po statusie | ⏳ |
| JOB-006 | "cancel job download-abc123" | Cancelled job: download-abc123 | NL → /job_cancel | ⏳ |
| JOB-007 | "how many jobs are running" | 1 job running: download-abc123 | Count z kontekstu | ⏳ |
| JOB-008 | "show completed jobs" | 1) download-ghi789 (completed 1h ago) | Filtrowanie po statusie | ⏳ |
| JOB-009 | "cancel all failed jobs" | Cancelled 2 failed jobs: job-xxx, job-yyy | Batch cancel | ⏳ |
| JOB-010 | "any failed jobs" | 1) job-xxx (failed 30min ago, error: connection timeout) | Lista failed | ⏳ |

---

## MEDIA DOMAIN

### Basic Media Commands

| ID | Input | Expected Output | Notes | Status |
|----|-------|-----------------|-------|--------|
| MED-001 | `/media` | Media library: 1) /movies/Inception.mkv (4.2GB, movie) 2) /tv/Breaking.Bad.S01E01.mkv (1.5GB, tv) 3) /music/ACDC - Back in Black.mp3 (150MB, music) | Lista plików | ⏳ |
| MED-002 | `/media --type movie` | Movies: 1) /movies/Inception.mkv (4.2GB) 2) /movies/The.Matrix.mkv (3.8GB) | Filtrowanie po typie | ⏳ |
| MED-003 | `/media --type tv` | TV Shows: 1) /tv/Breaking.Bad.S01E01.mkv (1.5GB) 2) /tv/Game.of.Thrones.S01E01.mkv (2.1GB) | Filtrowanie po typie | ⏳ |
| MED-004 | `/media --type music` | Music: 1) /music/ACDC - Back in Black.mp3 (150MB) 2) /music/Pink.Floyd - Dark.Side.of.the.Moon.flac (800MB) | Filtrowanie po typie | ⏳ |

### Natural Language - Media

| ID | Input | Expected Output | Notes | Status |
|----|-------|-----------------|-------|--------|
| MED-005 | "what media files do I have" | Lista wszystkich plików (jak /media) | NL → /media | ⏳ |
| MED-006 | "show movies in library" | Lista movies (jak /media --type movie) | NL → /media --type movie | ⏳ |
| MED-007 | "show tv shows" | Lista tv shows | NL → /media --type tv | ⏳ |
| MED-008 | "show music" | Lista music files | NL → /media --type music | ⏳ |
| MED-009 | "do I have Inception" | Yes, /movies/Inception.mkv (4.2GB) | Search by name | ⏳ |
| MED-010 | "any ACDC music" | Yes, /music/ACDC - Back in Black.mp3 (150MB) | Search by artist | ⏳ |
| MED-011 | "how many movies do I have" | 2 movies in library | Count z kontekstu | ⏳ |
| MED-012 | "what's the largest file in my library" | /movies/Inception.mkv (4.2GB) | Max size | ⏳ |
| MED-013 | "show me Breaking Bad episodes" | /tv/Breaking.Bad.S01E01.mkv (1.5GB) | Search by show name | ⏳ |
| MED-014 | "any new media added today" | 1) /movies/New.Movie.mkv (3.5GB, added 2h ago) | Filter by date | ⏳ |

---

## TORRENT DOMAIN

### Basic Torrent Commands

| ID | Input | Expected Output | Notes | Status |
|----|-------|-----------------|-------|--------|
| TOR-001 | `/search linux` | 10 results: 1) ubuntu-24.04-desktop.iso (4.7GB, 500 seeds) 2) debian-12.5.0-amd64-netinst.iso (600MB, 300 seeds) 3) archlinux-2024.03.01-x86_64.iso (800MB, 200 seeds) ... | Wyniki z Jackett | ⏳ |
| TOR-002 | `/search ubuntu` | 10 results: 1) ubuntu-24.04-desktop.iso (4.7GB, 500 seeds) 2) ubuntu-22.04-server.iso (1.5GB, 300 seeds) ... | Wyniki z Jackett | ⏳ |
| TOR-003 | `/search "pink floyd"` | 10 results: 1) Pink.Floyd-The.Wall.flac (1.2GB, 150 seeds) 2) Pink.Floyd-Dark.Side.of.the.Moon.flac (800MB, 200 seeds) ... | Wyniki z Jackett | ⏳ |
| TOR-004 | `/torrents` | qBittorrent torrents: 1) ubuntu.iso (downloading, 45%, 2.1GB/4.7GB, ETA 15min) 2) fedora.img (completed, 100%, seeding, 3.2GB, ratio 1.5) | Lista z qBittorrent | ⏳ |
| TOR-005 | `/select 1` | Selected: ubuntu-24.04-desktop.iso, download started | Wybór z wyników /search | ⏳ |
| TOR-006 | `/select 2` | Selected: debian-12.5.0-amd64-netinst.iso, download started | Wybór z wyników /search | ⏳ |
| TOR-007 | `/more` | Next page of results (11-20) | Paginacja | ⏳ |
| TOR-008 | `/cancel_search` | Search session cancelled | Anulowanie sesji | ⏳ |
| TOR-009 | `/torrent_pause ubuntu.iso` | Paused: ubuntu.iso | Pauza torrenta | ⏳ |
| TOR-010 | `/torrent_resume ubuntu.iso` | Resumed: ubuntu.iso | Wznowienie torrenta | ⏳ |
| TOR-011 | `/torrent_delete ubuntu.iso` | Deleted: ubuntu.iso from qBittorrent (files kept) | Usunięcie torrenta | ⏳ |
| TOR-012 | `/download_candidate ubuntu` | Searched "ubuntu", selected best match (ubuntu-24.04-desktop.iso, 500 seeds), download started | Auto-select best | ⏳ |
| TOR-013 | `/download_candidate fedora` | Searched "fedora", selected best match (Fedora-Workstation-Live-x86_64-39.iso, 300 seeds), download started | Auto-select best | ⏳ |

### Natural Language - Torrent

| ID | Input | Expected Output | Notes | Status |
|----|-------|-----------------|-------|--------|
| TOR-014 | "search for linux torrent" | 10 results (jak /search linux) | NL → torrent.search | ⏳ |
| TOR-015 | "search for ubuntu iso" | 10 results (jak /search ubuntu) | NL → torrent.search | ⏳ |
| TOR-016 | "search for pink floyd music" | 10 results (jak /search "pink floyd") | NL → torrent.search | ⏳ |
| TOR-017 | "show my torrents" | Lista z qBittorrent (jak /torrents) | NL → /torrents | ⏳ |
| TOR-018 | "download ubuntu" | Searched, selected best, download started | NL → download_candidate | ⏳ |
| TOR-019 | "download fedora" | Searched, selected best, download started | NL → download_candidate | ⏳ |
| TOR-020 | "pause ubuntu torrent" | Paused: ubuntu.iso | NL → /torrent_pause | ⏳ |
| TOR-021 | "resume ubuntu torrent" | Resumed: ubuntu.iso | NL → /torrent_resume | ⏳ |
| TOR-022 | "delete ubuntu torrent" | Deleted: ubuntu.iso | NL → /torrent_delete | ⏳ |
| TOR-023 | "search for arch linux and download the best one" | Searched, selected archlinux-2024.03.01-x86_64.iso (200 seeds), download started | Multi-step NL | ⏳ |
| TOR-024 | "find torrents with most seeds for ubuntu" | Results sorted by seed count | NL → torrent.search (sorted) | ⏳ |

---

## BOT DOMAIN

### Basic Bot Commands

| ID | Input | Expected Output | Notes | Status |
|----|-------|-----------------|-------|--------|
| BOT-001 | `/diag` | Diagnostic status: Ollama: OK (connected, model: qwen3:0.6b), Jackett: OK (15 indexers configured), qBittorrent: OK (3 active torrents), Surveillance: OK (recording), TTS: OK (pl_PL voice loaded) | Pełny diagnostic | ⏳ |
| BOT-002 | `/plugins` | Hot plugins: 0 loaded (plugins/hot/ directory empty) | Status pluginów | ⏳ |

### Natural Language - Bot

| ID | Input | Expected Output | Notes | Status |
|----|-------|-----------------|-------|--------|
| BOT-003 | "run diagnostics" | Diagnostic status (jak /diag) | NL → /diag | ⏳ |
| BOT-004 | "check system health" | Diagnostic status (jak /diag) | NL → /diag | ⏳ |
| BOT-005 | "show plugin status" | Hot plugins status (jak /plugins) | NL → /plugins | ⏳ |

---

## Test Execution Log

| Date | Tester | Test ID | Result | Notes |
|------|--------|---------|--------|-------|
| | | | | |

---

## Summary

- **Total Basic Tests**: 79
- **System**: 20 tests
- **Downloads**: 30 tests
- **Jobs**: 10 tests
- **Media**: 14 tests
- **Torrent**: 24 tests
- **Bot**: 5 tests

## Next Steps

1. Run all Basic tests manually in Telegram
2. Document results in Test Execution Log
3. Fix any failing tests
4. Once all Basic tests pass, add Medium tests
5. Then add Advanced tests
