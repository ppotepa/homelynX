#!/usr/bin/env bash
# TorrentBot2 — 50 CLI test scenarios (same pipeline path as Telegram)
# Usage: ./scripts/cli-scenarios.sh [filter] [run|plan]
#   filter  — substring match on scenario id/name (optional)
#   run|plan — agent run (default) or agent plan only

set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ -f .env ]]; then set -a; source .env; set +a; fi

MODE="${2:-run}"
FILTER="${1:-}"
CLI=(dotnet run --project src/TorrentBot.Adapters.Cli --no-build -- agent "$MODE")
USER="${TEST_USER:-8153696940}"
SESSION="cli-scenarios-$(date +%s)"
PASS=0
FAIL=0
SKIP=0
RESULTS_DIR="${TMPDIR:-/tmp}/torrentbot-scenarios-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$RESULTS_DIR"

run_scenario() {
  local id="$1" text="$2" expect="$3"
  if [[ -n "$FILTER" && "$id" != *"$FILTER"* && "$text" != *"$FILTER"* ]]; then
    return 0
  fi

  local log="$RESULTS_DIR/${id}.log"
  local start end elapsed rc=0
  start=$(date +%s%3N)
  if ! "${CLI[@]}" "$text" --user "$USER" >"$log" 2>&1; then rc=1; fi
  end=$(date +%s%3N)
  elapsed=$((end - start))

  local body
  body=$(cat "$log")
  if [[ -n "$expect" ]]; then
    if echo "$body" | grep -qiE "$expect"; then
      echo "PASS  $id (${elapsed}ms) expect=/$expect/"
      PASS=$((PASS + 1))
    else
      echo "FAIL  $id (${elapsed}ms) expect=/$expect/ got: $(echo "$body" | head -1 | cut -c1-80)"
      FAIL=$((FAIL + 1))
    fi
  else
    if [[ $rc -eq 0 ]]; then
      echo "PASS  $id (${elapsed}ms)"
      PASS=$((PASS + 1))
    else
      echo "FAIL  $id (${elapsed}ms) exit=$rc"
      FAIL=$((FAIL + 1))
    fi
  fi
}

echo "Building CLI..."
dotnet build src/TorrentBot.Adapters.Cli/TorrentBot.Adapters.Cli.csproj -q
echo "Results: $RESULTS_DIR"
echo "Mode: agent $MODE | User: $USER"
echo "---"

# === A. NL intent / planowanie (1-15) ===
run_scenario "S01" "download ubuntu 22 iso" "torrent|Wyniki|ubuntu|search"
run_scenario "S02" "download ubuntu iso 22" "torrent|Wyniki|ubuntu|search|Brak"
run_scenario "S03" "pobierz ubuntu 22" "ubuntu|search|Wyniki|Brak"
run_scenario "S04" "get debian 12 iso" "debian|search|Wyniki|Brak"
run_scenario "S05" "search for linux mint" "mint|search|Wyniki|Brak"
run_scenario "S06" "szukaj windows 11" "windows|search|Wyniki|Brak"
run_scenario "S07" "znajdz fedora workstation" "fedora|search|Wyniki|Brak"
run_scenario "S08" "find arch linux" "arch|search|Wyniki|Brak"
run_scenario "S09" "pokaż pobierania" "pobier|download|torrent|lista"
run_scenario "S10" "pokaż torrenty" "torrent|lista|status"
run_scenario "S11" "list all commands" "command|help|capabilit"
run_scenario "S12" "show downloads" "download|pobier|torrent"
run_scenario "S13" "test" ""
run_scenario "S14" "download list" "list|help|command"
run_scenario "S15" "download status" "status|download|torrent"

# === B. Explicit slash commands (16-30) ===
run_scenario "S16" "/health" "healthy|health|ok"
run_scenario "S17" "/help" "command|help|/"
run_scenario "S18" "/download_search ubuntu" "ubuntu|Wyniki|Brak|torrent"
run_scenario "S19" "/download_search ubuntu 22 iso" "ubuntu|Wyniki|Brak"
run_scenario "S20" "/search acdc" "acdc|Wyniki|Brak|torrent"
run_scenario "S21" "/list" "command|help|/"
run_scenario "S22" "/jobs" "job|brak|empty|lista"
run_scenario "S23" "/commands" "command|help|/"
run_scenario "S25" "/capabilities" "capabilit|torrent|download"
run_scenario "S26" "/torrent_search linux" "linux|Wyniki|Brak"
run_scenario "S27" "/download_search" "query|required|param|error"
run_scenario "S28" "/select 1" "select|index|search|brak|pending"
run_scenario "S29" "/more" "more|search|brak|page"
run_scenario "S30" "/cancel_search" "cancel|anul|search"

# === C. Multi-turn / session (31-40) — run mode only ===
if [[ "$MODE" == "run" ]]; then
  SESSION_STORE="$RESULTS_DIR/session.log"
  run_session() {
    local id="$1" text="$2" expect="$3"
    local log="$RESULTS_DIR/${id}.log"
    dotnet run --project src/TorrentBot.Adapters.Cli --no-build -- run "$text" --user "$USER" --session "$SESSION" >"$log" 2>&1 || true
    local body; body=$(cat "$log")
    if echo "$body" | grep -qiE "$expect"; then
      echo "PASS  $id"; PASS=$((PASS + 1))
    else
      echo "FAIL  $id got: $(echo "$body" | head -1 | cut -c1-80)"; FAIL=$((FAIL + 1))
    fi
  }
  run_session "S31" "download ubuntu 22 iso" "ubuntu|Wyniki|Brak"
  run_session "S32" "wybierz drugi" "select|index|pending|brak|confirm"
  run_session "S33" "/download_search debian" "debian|Wyniki|Brak"
  run_session "S34" "/select 1" "confirm|download|pending|brak|error"
  run_session "S35" "tak" "confirm|download|pending|brak"
  run_session "S36" "/download_search xyznonexistent99999" "Brak wynikow|0|Wyniki"
  run_session "S37" "/health" "healthy|health"
  run_session "S38" "pokaż status" "status|download|torrent"
  run_session "S39" "/cancel_search" "cancel|anul"
  run_session "S40" "/help" "help|command"
else
  echo "SKIP  S31-S40 (multi-turn requires run mode)"
  SKIP=$((SKIP + 10))
fi

# === D. Capability direct / edge cases (41-50) ===
CAP_CALL=(dotnet run --project src/TorrentBot.Adapters.Cli --no-build -- capability call)
for spec in \
  "S42|torrent.search|query=ubuntu|ubuntu|Wyniki|Brak" \
  "S43|torrent.search|query=ubuntu 22 iso|ubuntu|Wyniki|Brak" \
  "S44|system.health||healthy|health" \
  "S45|system.help||help|command" \
  "S46|torrent.list||torrent|lista|brak" \
  "S47|download.list||download|pobier|brak"; do
  IFS='|' read -r sid cap params expect <<<"$spec"
  log="$RESULTS_DIR/${sid}.log"
  args=("$cap" --user "$USER")
  if [[ -n "$params" ]]; then args+=(--param "$params"); fi
  if "${CAP_CALL[@]}" "${args[@]}" >"$log" 2>&1; then rc=0; else rc=1; fi
  body=$(cat "$log")
  if echo "$body" | grep -qiE "$expect"; then
    echo "PASS  $sid"; PASS=$((PASS + 1))
  else
    echo "FAIL  $sid got: $(echo "$body" | head -1 | cut -c1-80)"; FAIL=$((FAIL + 1))
  fi
done

# S48-S50: plan-only checks (fast, no jackett execution)
for spec in \
  "S48|download ubuntu 22 iso|ubuntu 22 iso" \
  "S49|download ubuntu iso 22|ubuntu iso 22" \
  "S50|pobierz debian 12|debian 12"; do
  IFS='|' read -r sid text query <<<"$spec"
  log="$RESULTS_DIR/${sid}.log"
  if dotnet run --project src/TorrentBot.Adapters.Cli --no-build -- agent plan "$text" --user "$USER" >"$log" 2>&1; then
    body=$(cat "$log")
    if echo "$body" | grep -qiE "Search:|Wyniki:|ubuntu|debian"; then
      echo "PASS  $sid plan query=$query"; PASS=$((PASS + 1))
    else
      echo "FAIL  $sid plan: $(echo "$body" | head -1 | cut -c1-80)"; FAIL=$((FAIL + 1))
    fi
  else
    echo "FAIL  $sid plan exit!=0"; FAIL=$((FAIL + 1))
  fi
done

echo "---"
echo "SUMMARY: PASS=$PASS FAIL=$FAIL SKIP=$SKIP"
echo "Logs: $RESULTS_DIR"
exit $((FAIL > 0 ? 1 : 0))
