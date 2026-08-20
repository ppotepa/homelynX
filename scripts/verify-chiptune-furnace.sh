#!/usr/bin/env bash
set -euo pipefail

renderer="${1:-${CHIPTUNE_RENDERER_PATH:-}}"
if [[ -z "$renderer" || ! -x "$renderer" ]]; then
  printf 'usage: %s /path/to/homelynx-chiptune-renderer\n' "$0" >&2
  exit 2
fi

workdir="$(mktemp -d "${TMPDIR:-/tmp}/homelynx-chiptune-smoke.XXXXXX")"
trap 'rm -rf "$workdir"' EXIT

peak_of() {
  od -An -v -td2 -j44 "$1" | awk '{ for (i = 1; i <= NF; i++) { value = $i; if (value < 0) value = -value; if (value > peak) peak = value } } END { print peak + 0 }'
}

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
  peak="$(peak_of "$output")"
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

# Regression for the old NoteOff/NoteOn collision. Four contiguous notes on
# one hardware voice must all reach the tracker compiler. Previously the OFF
# from note N occupied note N+1's row and every following onset could vanish.
adjacent_output="$workdir/adjacent.wav"
adjacent_request='{"chip":"pcspeaker","bpm":120,"sampleRate":44100,"endTick":1920,"notes":[{"voice":0,"startTick":0,"durationTick":480,"pitch":60,"velocity":100,"instrumentId":0,"instrument":"lead","voiceClass":"beeper"},{"voice":0,"startTick":480,"durationTick":480,"pitch":62,"velocity":100,"instrumentId":0,"instrument":"lead","voiceClass":"beeper"},{"voice":0,"startTick":960,"durationTick":480,"pitch":64,"velocity":100,"instrumentId":0,"instrument":"lead","voiceClass":"beeper"},{"voice":0,"startTick":1440,"durationTick":480,"pitch":65,"velocity":100,"instrumentId":0,"instrument":"lead","voiceClass":"beeper"}]}'
if ! printf '%s' "$adjacent_request" | "$renderer" "$adjacent_output" >"$workdir/adjacent.stdout" 2>"$workdir/adjacent.stderr"; then
  printf 'FAIL adjacent-notes: renderer exited non-zero\n%s\n' "$(tail -n 3 "$workdir/adjacent.stderr")" >&2
  exit 1
fi
if ! grep -q '"notesReceived":4' "$workdir/adjacent.stdout" || ! grep -q '"notesWritten":4' "$workdir/adjacent.stdout"; then
  printf 'FAIL adjacent-notes: compiler did not retain every onset\n%s\n' "$(cat "$workdir/adjacent.stdout")" >&2
  exit 1
fi
printf 'PASS adjacent-notes (%s)\n' "$(cat "$workdir/adjacent.stdout")"

# Game Boy wave-channel regression: a bass assigned only to voice 2 must be
# audible. This used to select Furnace nullWave and rendered an effectively
# empty bass part.
gb_wave_output="$workdir/gb-wave.wav"
gb_wave_request='{"chip":"gbc","bpm":120,"sampleRate":44100,"endTick":960,"notes":[{"voice":2,"startTick":0,"durationTick":960,"pitch":48,"velocity":110,"instrumentId":3,"instrument":"bass","voiceClass":"wave"}]}'
if ! printf '%s' "$gb_wave_request" | "$renderer" "$gb_wave_output" >"$workdir/gb-wave.stdout" 2>"$workdir/gb-wave.stderr"; then
  printf 'FAIL gb-wave: renderer exited non-zero\n%s\n' "$(tail -n 3 "$workdir/gb-wave.stderr")" >&2
  exit 1
fi
gb_wave_peak="$(peak_of "$gb_wave_output")"
if [[ "$gb_wave_peak" -le 100 ]]; then
  printf 'FAIL gb-wave: wave channel is silent, peak=%s\n' "$gb_wave_peak" >&2
  exit 1
fi
printf 'PASS gb-wave (peak=%s)\n' "$gb_wave_peak"

# SNES tuning regression: soft_lead is deliberately a single-cycle sine. A
# MIDI C4 should therefore land close to concert middle C instead of the old
# 1024-sample cycle that was roughly four octaves too low.
snes_tone_output="$workdir/snes-c4.wav"
snes_tone_request='{"chip":"snes","bpm":120,"sampleRate":44100,"endTick":1200,"notes":[{"voice":0,"startTick":0,"durationTick":960,"pitch":60,"velocity":110,"instrumentId":1,"instrument":"soft_lead","voiceClass":"sample"}]}'
if ! printf '%s' "$snes_tone_request" | "$renderer" "$snes_tone_output" >"$workdir/snes-c4.stdout" 2>"$workdir/snes-c4.stderr"; then
  printf 'FAIL snes-c4: renderer exited non-zero\n%s\n' "$(tail -n 3 "$workdir/snes-c4.stderr")" >&2
  exit 1
fi
python3 - "$snes_tone_output" <<'PY'
import struct, sys, wave
path = sys.argv[1]
with wave.open(path, 'rb') as w:
    rate = w.getframerate()
    channels = w.getnchannels()
    raw = w.readframes(w.getnframes())
samples = struct.unpack('<' + 'h' * (len(raw) // 2), raw)
left = samples[::channels]
start = min(len(left), int(rate * 0.04))
end = min(len(left), int(rate * 0.34))
segment = left[start:end]
if len(segment) < rate // 20:
    raise SystemExit('SNES tone fixture is too short')
up = sum(1 for a, b in zip(segment, segment[1:]) if a <= 0 < b)
duration = len(segment) / rate
freq = up / duration
if not 220.0 <= freq <= 310.0:
    raise SystemExit(f'SNES C4 tuning outside sanity range: {freq:.1f} Hz')
print(f'PASS snes-c4 frequency={freq:.1f}Hz')
PY

printf 'All %s Furnace chiptune profiles rendered successfully.\n' "${#chips[@]}"
