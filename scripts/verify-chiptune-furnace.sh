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
  request="$(printf '{"chip":"%s","bpm":140,"sampleRate":44100,"endTick":960,"vibrato":4,"filter":900,"notes":[{"voice":0,"startTick":0,"durationTick":480,"pitch":60,"velocity":100,"volume":110,"modulation":80,"pan":32,"pitchBend":9000,"pitchBendRange":2,"pitchBends":[{"tick":240,"value":12288}],"controllerChanges":[{"tick":360,"volume":64,"expression":100,"pan":96,"modulation":48,"aftertouch":16}],"noteCutTicks":120,"noteDelayTicks":15,"retrigger":2,"pitchSlide":3,"volumeSlide":-2,"instrumentId":0,"instrument":"lead"}]}' "$chip")"
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
  channels="$(od -An -tu2 -j22 -N2 "$output" | tr -d ' ')"
  sample_rate="$(od -An -tu4 -j24 -N4 "$output" | tr -d ' ')"
  if [[ "$channels" != "2" || "$sample_rate" != "44100" ]]; then
    printf 'FAIL %s: expected stereo 44100 Hz, got channels=%s rate=%s\n' "$chip" "$channels" "$sample_rate" >&2
    exit 1
  fi
  peak="$(od -An -v -td2 -j44 "$output" | awk '{ for (i = 1; i <= NF; i++) { value = $i; if (value < 0) value = -value; if (value > peak) peak = value } } END { print peak + 0 }')"
  if [[ "$peak" -le 100 || "$peak" -ge 32767 ]]; then
    printf 'FAIL %s: invalid audio level peak=%s\n' "$chip" "$peak" >&2
    exit 1
  fi
  fingerprint="$(sha256sum "$output" | awk '{print $1}')"
  printf 'PASS %s (%s bytes, peak=%s, sha256=%s)\n' "$chip" "$(wc -c < "$output")" "$peak" "$fingerprint"
done

# Multi-voice quality smoke: one simultaneous four-part GB/GBC chord must
# reach separate tracker channels without a native cell collision.
multi_output="$workdir/gbc-multi.wav"
multi_request='{"chip":"gbc","bpm":140,"sampleRate":44100,"endTick":960,"notes":[{"voice":0,"startTick":0,"durationTick":480,"pitch":72,"velocity":110,"instrumentId":0,"instrument":"lead","voiceClass":"pulse"},{"voice":1,"startTick":0,"durationTick":480,"pitch":67,"velocity":90,"instrumentId":4,"instrument":"strings","voiceClass":"pulse"},{"voice":2,"startTick":0,"durationTick":480,"pitch":48,"velocity":100,"instrumentId":3,"instrument":"bass","voiceClass":"wave"},{"voice":3,"startTick":0,"durationTick":120,"pitch":36,"velocity":100,"instrumentId":16,"instrument":"kick","voiceClass":"noise"}]}'
if ! printf '%s' "$multi_request" | "$renderer" "$multi_output" >"$workdir/gbc-multi.stdout" 2>"$workdir/gbc-multi.stderr"; then
  printf 'FAIL gbc-multi: renderer exited non-zero\n%s\n' "$(tail -n 3 "$workdir/gbc-multi.stderr")" >&2
  exit 1
fi
if [[ ! -s "$multi_output" || "$(wc -c < "$multi_output")" -le 44 ]]; then
  printf 'FAIL gbc-multi: output is empty\n' >&2
  exit 1
fi
printf 'PASS gbc-multi (%s bytes)\n' "$(wc -c < "$multi_output")"

printf 'All %s Furnace chiptune profiles rendered successfully.\n' "${#chips[@]}"
