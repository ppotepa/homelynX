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
multi_request='{"chip":"gbc","bpm":140,"sampleRate":44100,"endTick":960,"notes":[{"voice":0,"startTick":0,"durationTick":480,"pitch":72,"velocity":110,"instrumentId":0,"instrument":"lead","voiceClass":"pulse"},{"voice":1,"startTick":0,"durationTick":480,"pitch":67,"velocity":90,"instrumentId":1,"instrument":"strings","voiceClass":"pulse"},{"voice":2,"startTick":0,"durationTick":480,"pitch":48,"velocity":100,"instrumentId":2,"instrument":"bass","voiceClass":"wave"},{"voice":3,"startTick":0,"durationTick":120,"pitch":36,"velocity":100,"instrumentId":3,"instrument":"kick","voiceClass":"noise"}]}'
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
# one hardware voice must all reach the tracker compiler.
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

# Game Boy wave-channel regression: a bass assigned only to voice 2 must be audible.
gb_wave_output="$workdir/gb-wave.wav"
gb_wave_request='{"chip":"gbc","bpm":120,"sampleRate":44100,"endTick":960,"notes":[{"voice":2,"startTick":0,"durationTick":960,"pitch":48,"velocity":110,"instrumentId":0,"instrument":"bass","voiceClass":"wave"}]}'
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

# SNES tuning regression: soft_lead is a single-cycle waveform. C4 should
# remain close to middle C rather than the old four-octaves-low sample pitch.
snes_tone_output="$workdir/snes-c4.wav"
snes_tone_request='{"chip":"snes","bpm":120,"sampleRate":44100,"endTick":1200,"notes":[{"voice":0,"startTick":0,"durationTick":960,"pitch":60,"velocity":110,"instrumentId":0,"instrument":"soft_lead","voiceClass":"sample"}]}'
if ! printf '%s' "$snes_tone_request" | "$renderer" "$snes_tone_output" >"$workdir/snes-c4.stdout" 2>"$workdir/snes-c4.stderr"; then
  printf 'FAIL snes-c4: renderer exited non-zero\n%s\n' "$(tail -n 3 "$workdir/snes-c4.stderr")" >&2
  exit 1
fi
python3 - "$snes_tone_output" <<'PY'
import struct, sys, wave
path = sys.argv[1]
with wave.open(path, 'rb') as w:
    rate = w.getframerate(); channels = w.getnchannels(); raw = w.readframes(w.getnframes())
samples = struct.unpack('<' + 'h' * (len(raw) // 2), raw)
left = samples[::channels]
segment = left[int(rate * 0.04):min(len(left), int(rate * 0.34))]
if len(segment) < rate // 20: raise SystemExit('SNES tone fixture is too short')
up = sum(1 for a, b in zip(segment, segment[1:]) if a <= 0 < b)
freq = up / (len(segment) / rate)
if not 220.0 <= freq <= 310.0: raise SystemExit(f'SNES C4 tuning outside sanity range: {freq:.1f} Hz')
print(f'PASS snes-c4 frequency={freq:.1f}Hz')
PY

# SNES percussion sample map: GM percussion note chooses drum identity, not
# transposition. Rendering the same kick patch at two note numbers must produce
# exactly the same PCM payload.
for pitch in 36 48; do
  output="$workdir/snes-kick-$pitch.wav"
  request="$(printf '{"chip":"snes","bpm":120,"sampleRate":44100,"endTick":960,"notes":[{"voice":0,"startTick":0,"durationTick":480,"pitch":%s,"velocity":110,"instrumentId":0,"instrument":"kick","voiceClass":"sample"}]}' "$pitch")"
  printf '%s' "$request" | "$renderer" "$output" >"$workdir/snes-kick-$pitch.stdout" 2>"$workdir/snes-kick-$pitch.stderr"
done
python3 - "$workdir/snes-kick-36.wav" "$workdir/snes-kick-48.wav" <<'PY'
import sys, wave
def frames(path):
    with wave.open(path,'rb') as w: return w.readframes(w.getnframes())
a,b=map(frames,sys.argv[1:])
if a != b: raise SystemExit('SNES drum sample pitch still depends on GM drum note')
print('PASS snes-fixed-drum-pitch')
PY

# NES DPCM uses a fixed DPCM pitch from its note map for the same reason.
for pitch in 36 48; do
  output="$workdir/nes-kick-$pitch.wav"
  request="$(printf '{"chip":"nes","bpm":120,"sampleRate":44100,"endTick":960,"notes":[{"voice":4,"startTick":0,"durationTick":480,"pitch":%s,"velocity":110,"instrumentId":0,"instrument":"kick","voiceClass":"dpcm"}]}' "$pitch")"
  printf '%s' "$request" | "$renderer" "$output" >"$workdir/nes-kick-$pitch.stdout" 2>"$workdir/nes-kick-$pitch.stderr"
done
python3 - "$workdir/nes-kick-36.wav" "$workdir/nes-kick-48.wav" <<'PY'
import sys, wave
def frames(path):
    with wave.open(path,'rb') as w: return w.readframes(w.getnframes())
a,b=map(frames,sys.argv[1:])
if a != b: raise SystemExit('NES DPCM pitch still depends on GM drum note')
print('PASS nes-fixed-dpcm-pitch')
PY

# The old TIA and POKEY patch maps accidentally selected noise for bass. A bass
# tone must now show stable periodic zero-crossing intervals.
for chip in atari2600 pokey; do
  output="$workdir/$chip-bass.wav"
  request="$(printf '{"chip":"%s","bpm":120,"sampleRate":44100,"endTick":1200,"notes":[{"voice":0,"startTick":0,"durationTick":960,"pitch":48,"velocity":110,"instrumentId":0,"instrument":"bass","voiceClass":"%s"}]}' "$chip" "$([[ "$chip" == atari2600 ]] && echo tia || echo pokey)")"
  printf '%s' "$request" | "$renderer" "$output" >"$workdir/$chip-bass.stdout" 2>"$workdir/$chip-bass.stderr"
  python3 - "$output" "$chip" <<'PY'
import statistics, struct, sys, wave
path, chip = sys.argv[1], sys.argv[2]
with wave.open(path,'rb') as w:
    rate=w.getframerate(); ch=w.getnchannels(); raw=w.readframes(w.getnframes())
s=struct.unpack('<'+'h'*(len(raw)//2), raw)[::ch]
s=s[int(rate*.06):min(len(s),int(rate*.42))]
cross=[i for i,(a,b) in enumerate(zip(s,s[1:])) if a<=0<b]
intervals=[b-a for a,b in zip(cross,cross[1:]) if b>a]
if len(intervals)<6: raise SystemExit(f'{chip} bass has too few periodic crossings')
mean=statistics.mean(intervals); cv=statistics.pstdev(intervals)/mean
if cv>.35: raise SystemExit(f'{chip} bass looks noise-like: crossing CV={cv:.3f}')
print(f'PASS {chip}-bass-periodicity cv={cv:.3f}')
PY
done

# Native controller fidelity: volume zero must make the latter part genuinely silent.
volume_output="$workdir/native-volume-zero.wav"
volume_request='{"chip":"snes","bpm":120,"sampleRate":44100,"endTick":1200,"notes":[{"voice":0,"startTick":0,"durationTick":960,"pitch":69,"velocity":110,"volume":127,"expression":127,"instrumentId":0,"instrument":"soft_lead","voiceClass":"sample","controllerChanges":[{"tick":480,"volume":0,"expression":127,"pan":64,"modulation":0,"aftertouch":0}]}]}'
printf '%s' "$volume_request" | "$renderer" "$volume_output" >"$workdir/native-volume-zero.stdout" 2>"$workdir/native-volume-zero.stderr"
python3 - "$volume_output" <<'PY'
import math, struct, sys, wave
with wave.open(sys.argv[1],'rb') as w:
    rate=w.getframerate(); ch=w.getnchannels(); raw=w.readframes(w.getnframes())
s=struct.unpack('<'+'h'*(len(raw)//2),raw)[::ch]
def rms(v): return math.sqrt(sum(x*x for x in v)/max(1,len(v)))
first=s[int(rate*.04):int(rate*.22)]
second=s[int(rate*.30):int(rate*.46)]
if rms(first)<100: raise SystemExit('native volume-zero fixture has no audible first half')
if rms(second)>5: raise SystemExit(f'CC7=0 did not silence native output: rms={rms(second):.2f}')
print('PASS native-volume-zero')
PY

printf 'All %s Furnace chiptune profiles rendered successfully.\n' "${#chips[@]}"
