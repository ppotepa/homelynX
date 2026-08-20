#!/usr/bin/env bash
set -euo pipefail

renderer="${1:-${CHIPTUNE_RENDERER_PATH:-}}"
if [[ -z "$renderer" || ! -x "$renderer" ]]; then
  printf 'usage: %s /path/to/homelynx-chiptune-renderer\n' "$0" >&2
  exit 2
fi

workdir="$(mktemp -d "${TMPDIR:-/tmp}/homelynx-chiptune-bank.XXXXXX")"
trap 'rm -rf "$workdir"' EXIT

render_patch() {
  local chip="$1" patch="$2" voice="$3" voice_class="$4" pitch="$5" output="$6"
  local request
  request="$(printf '{"chip":"%s","bpm":120,"sampleRate":44100,"endTick":1200,"notes":[{"voice":%s,"startTick":0,"durationTick":960,"pitch":%s,"velocity":112,"volume":127,"expression":127,"pan":64,"instrumentId":0,"instrument":"%s","voiceClass":"%s"}]}' "$chip" "$voice" "$pitch" "$patch" "$voice_class")"
  if ! printf '%s' "$request" | "$renderer" "$output" >"$output.stdout" 2>"$output.stderr"; then
    printf 'FAIL %s/%s: renderer exited non-zero\n%s\n' "$chip" "$patch" "$(tail -n 5 "$output.stderr")" >&2
    exit 1
  fi
  if ! grep -q '"notesReceived":1' "$output.stdout" || ! grep -q '"notesWritten":1' "$output.stdout"; then
    printf 'FAIL %s/%s: note-retention report is invalid\n%s\n' "$chip" "$patch" "$(cat "$output.stdout")" >&2
    exit 1
  fi
}

assert_audible() {
  local path="$1" label="$2"
  python3 - "$path" "$label" <<'PY'
import math, struct, sys, wave
path,label=sys.argv[1:]
with wave.open(path,'rb') as w:
    ch=w.getnchannels(); raw=w.readframes(w.getnframes())
if not raw:
    raise SystemExit(f'{label}: empty PCM')
s=struct.unpack('<'+'h'*(len(raw)//2),raw)
mono=s[::ch]
peak=max(abs(x) for x in mono)
rms=math.sqrt(sum(x*x for x in mono)/max(1,len(mono)))
if peak <= 100 or rms <= 8:
    raise SystemExit(f'{label}: effectively silent peak={peak} rms={rms:.2f}')
print(f'PASS audible {label} peak={peak} rms={rms:.1f}')
PY
}

pcm_hash() {
  python3 - "$1" <<'PY'
import hashlib, sys, wave
with wave.open(sys.argv[1],'rb') as w:
    data=w.readframes(w.getnframes())
print(hashlib.sha256(data).hexdigest())
PY
}

assert_distinct() {
  local chip="$1" patch_a="$2" patch_b="$3" voice="$4" voice_class="$5" pitch="$6"
  local a="$workdir/${chip}-${patch_a}.wav" b="$workdir/${chip}-${patch_b}.wav"
  render_patch "$chip" "$patch_a" "$voice" "$voice_class" "$pitch" "$a"
  render_patch "$chip" "$patch_b" "$voice" "$voice_class" "$pitch" "$b"
  assert_audible "$a" "$chip/$patch_a"
  assert_audible "$b" "$chip/$patch_b"
  local ha hb
  ha="$(pcm_hash "$a")"; hb="$(pcm_hash "$b")"
  if [[ "$ha" == "$hb" ]]; then
    printf 'FAIL %s: patches %s and %s render identical PCM\n' "$chip" "$patch_a" "$patch_b" >&2
    exit 1
  fi
  printf 'PASS distinct %s %s!=%s\n' "$chip" "$patch_a" "$patch_b"
}

# Rich targets must be able to render every semantic melodic family produced
# by the MIDI/program and orchestration planners.
semantic_patches=(lead soft_lead pluck bass bell brass organ epiano strings pad reed flute)
for patch in "${semantic_patches[@]}"; do
  snes="$workdir/snes-$patch.wav"
  render_patch snes "$patch" 0 sample 69 "$snes"
  assert_audible "$snes" "snes/$patch"

  genesis="$workdir/genesis-$patch.wav"
  render_patch genesis "$patch" 0 fm 69 "$genesis"
  assert_audible "$genesis" "genesis/$patch"
done

# Each supported synthesis family must expose at least two genuinely distinct
# tonal patches. This guards against orchestration silently collapsing back to
# one generic lead even when the planner changes semantic instruments.
assert_distinct gbc lead soft_lead 0 pulse 69
assert_distinct nes lead pluck 0 pulse 69
assert_distinct sms lead pluck 0 pulse 69
assert_distinct snes lead bell 0 sample 69
assert_distinct genesis lead epiano 0 fm 69
assert_distinct pce lead bell 0 wavetable 69
assert_distinct c64_8580 lead bass 0 pulse 57
assert_distinct pokey lead bass 0 pokey 57
assert_distinct atari2600 lead bass 0 tia 57
assert_distinct pcspeaker lead pluck 0 beeper 69
assert_distinct zx_spectrum lead pluck 0 beeper 69

# A section-level instrument change must survive dense instrument IDs and be
# accepted on the same hardware voice without losing the second Note On.
switch_output="$workdir/genesis-instrument-switch.wav"
switch_request='{"chip":"genesis","bpm":120,"sampleRate":44100,"endTick":1920,"notes":[{"voice":0,"startTick":0,"durationTick":960,"pitch":69,"velocity":108,"instrumentId":0,"instrument":"soft_lead","voiceClass":"fm"},{"voice":0,"startTick":960,"durationTick":960,"pitch":69,"velocity":108,"instrumentId":1,"instrument":"brass","voiceClass":"fm"}]}'
if ! printf '%s' "$switch_request" | "$renderer" "$switch_output" >"$switch_output.stdout" 2>"$switch_output.stderr"; then
  printf 'FAIL genesis instrument switch\n%s\n' "$(tail -n 5 "$switch_output.stderr")" >&2
  exit 1
fi
if ! grep -q '"notesReceived":2' "$switch_output.stdout" || ! grep -q '"notesWritten":2' "$switch_output.stdout"; then
  printf 'FAIL genesis instrument switch lost an onset\n%s\n' "$(cat "$switch_output.stdout")" >&2
  exit 1
fi
assert_audible "$switch_output" "genesis/section-instrument-switch"

printf 'All chiptune semantic instrument-bank checks passed.\n'
