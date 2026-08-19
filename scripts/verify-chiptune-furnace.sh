#!/usr/bin/env bash
set -euo pipefail

renderer="${1:-${CHIPTUNE_RENDERER_PATH:-}}"
if [[ -z "$renderer" || ! -x "$renderer" ]]; then
  printf 'usage: %s /path/to/homelynx-chiptune-renderer\n' "$0" >&2
  exit 2
fi

workdir="$(mktemp -d "${TMPDIR:-/tmp}/homelynx-chiptune-smoke.XXXXXX")"
trap 'rm -rf "$workdir"' EXIT

chips=(gb gbc nes snes sms c64_6581 c64_8580 genesis pce atari2600 pokey pcspeaker zx_spectrum)
for chip in "${chips[@]}"; do
  output="$workdir/$chip.wav"
  request="$(printf '{"chip":"%s","bpm":140,"sampleRate":44100,"endTick":960,"vibrato":4,"filter":900,"notes":[{"voice":0,"startTick":0,"durationTick":480,"pitch":60,"velocity":100,"pan":32,"pitchBend":9000,"pitchBendRange":2,"instrumentId":0,"instrument":"lead"}]}' "$chip")"
  if ! printf '%s' "$request" | "$renderer" "$output" >"$workdir/$chip.stdout" 2>"$workdir/$chip.stderr"; then
    printf 'FAIL %s: renderer exited non-zero\n%s\n' "$chip" "$(tail -n 3 "$workdir/$chip.stderr")" >&2
    exit 1
  fi
  if [[ ! -s "$output" || "$(wc -c < "$output")" -le 44 ]]; then
    printf 'FAIL %s: output is empty\n' "$chip" >&2
    exit 1
  fi
  if [[ "$(dd if="$output" bs=1 count=4 2>/dev/null)" != "RIFF" ]]; then
    printf 'FAIL %s: output is not a RIFF/WAV file\n' "$chip" >&2
    exit 1
  fi
  printf 'PASS %s (%s bytes)\n' "$chip" "$(wc -c < "$output")"
done

printf 'All %s Furnace chiptune profiles rendered successfully.\n' "${#chips[@]}"
