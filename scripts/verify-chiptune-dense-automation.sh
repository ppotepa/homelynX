#!/usr/bin/env bash
set -euo pipefail

renderer="${1:-${CHIPTUNE_RENDERER_PATH:-}}"
if [[ -z "$renderer" || ! -x "$renderer" ]]; then
  printf 'usage: %s /path/to/homelynx-chiptune-renderer\n' "$0" >&2
  exit 2
fi

workdir="$(mktemp -d "${TMPDIR:-/tmp}/homelynx-chiptune-dense.XXXXXX")"
trap 'rm -rf "$workdir"' EXIT
output="$workdir/dense.wav"

# endTick just above Furnace's maximum row count forces ticksPerRow=2. Many
# controller/bend events deliberately collapse onto the same tracker rows.
# The compiler must coalesce each row to its final representable MIDI state
# instead of exhausting effect columns or dropping the Note On.
request='{
  "chip":"genesis",
  "bpm":300,
  "sampleRate":44100,
  "endTick":66000,
  "notes":[{
    "voice":0,
    "startTick":0,
    "durationTick":480,
    "pitch":69,
    "velocity":110,
    "volume":127,
    "expression":127,
    "pan":64,
    "pitchBend":8192,
    "pitchBendRange":12,
    "instrumentId":0,
    "instrument":"lead",
    "voiceClass":"fm",
    "pitchBends":[
      {"tick":99,"value":9000},{"tick":99,"value":10000},{"tick":99,"value":11000},
      {"tick":100,"value":12000},{"tick":100,"value":13000},{"tick":100,"value":14000},
      {"tick":101,"value":15000},{"tick":101,"value":12000},{"tick":101,"value":8192},
      {"tick":102,"value":7000},{"tick":102,"value":6000},{"tick":102,"value":8192}
    ],
    "controllerChanges":[
      {"tick":99,"volume":120,"expression":120,"pan":16,"modulation":10,"aftertouch":0},
      {"tick":99,"volume":110,"expression":110,"pan":24,"modulation":20,"aftertouch":0},
      {"tick":99,"volume":100,"expression":100,"pan":32,"modulation":30,"aftertouch":0},
      {"tick":100,"volume":90,"expression":100,"pan":48,"modulation":40,"aftertouch":0},
      {"tick":100,"volume":80,"expression":100,"pan":64,"modulation":50,"aftertouch":0},
      {"tick":100,"volume":70,"expression":100,"pan":80,"modulation":60,"aftertouch":0},
      {"tick":101,"volume":80,"expression":110,"pan":96,"modulation":70,"aftertouch":10},
      {"tick":101,"volume":90,"expression":115,"pan":112,"modulation":80,"aftertouch":20},
      {"tick":101,"volume":100,"expression":120,"pan":127,"modulation":90,"aftertouch":30},
      {"tick":102,"volume":110,"expression":123,"pan":96,"modulation":60,"aftertouch":10},
      {"tick":102,"volume":120,"expression":125,"pan":80,"modulation":30,"aftertouch":0},
      {"tick":102,"volume":127,"expression":127,"pan":64,"modulation":0,"aftertouch":0}
    ]
  }]
}'

if ! printf '%s' "$request" | "$renderer" "$output" >"$workdir/stdout" 2>"$workdir/stderr"; then
  printf 'FAIL dense-automation: renderer exited non-zero\n%s\n' "$(tail -n 10 "$workdir/stderr")" >&2
  exit 1
fi
if ! grep -q '"notesReceived":1' "$workdir/stdout" || ! grep -q '"notesWritten":1' "$workdir/stdout"; then
  printf 'FAIL dense-automation: note retention contract failed\n%s\n' "$(cat "$workdir/stdout")" >&2
  exit 1
fi
if [[ ! -s "$output" || "$(wc -c < "$output")" -le 44 ]]; then
  printf 'FAIL dense-automation: output is empty\n' >&2
  exit 1
fi
printf 'PASS dense-automation (%s)\n' "$(cat "$workdir/stdout")"
