#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TMP_DIR="$(mktemp -d)"
OUTPUT_FILE="${1:-$ROOT_DIR/concat.source.txt}"

cleanup() {
  rm -rf "$TMP_DIR"
}

trap cleanup EXIT

if ! command -v rsync >/dev/null 2>&1; then
  printf '[codecat-source-only] ERROR: rsync is required.\n' >&2
  exit 1
fi

if ! command -v codecat >/dev/null 2>&1; then
  printf '[codecat-source-only] ERROR: codecat is required.\n' >&2
  exit 1
fi

mkdir -p "$TMP_DIR/source"
rsync -a \
  --exclude-from="$ROOT_DIR/.codecatignore" \
  --exclude=".git/" \
  "$ROOT_DIR/" "$TMP_DIR/source/"

exec codecat "$TMP_DIR/source" --use-gitignore -o "$OUTPUT_FILE" --no-copy
