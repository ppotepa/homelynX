#!/bin/bash
# reliable-chain.sh - Run a torrent search -> select -> verify flow using reliable paths
# Usage: ./reliable-chain.sh "ubuntu 22 iso" [session-id]

set -euo pipefail

QUERY="${1:-ubuntu 22 iso}"
SESSION="${2:-reliable-$(date +%s)}"
CLI="dotnet run --no-build --project src/TorrentBot.Adapters.Cli --"

echo "=== Reliable chain for '$QUERY' (session: $SESSION) ==="

echo "1. /search"
$CLI run "/search $QUERY" --user admin --session "$SESSION" | tail -3

echo "2. /select 0"
$CLI run "/select 0" --user admin --session "$SESSION" | tail -2

echo "3. query downloads"
$CLI query downloads --user admin | tail -1

echo "4. /torrents"
$CLI run "/torrents" --user admin --session "$SESSION" | tail -2

echo "=== Done (use --dry-run caps for pause/resume/start in real tests) ==="
