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
declare -A golden=(
  [gb]=3afae13f2cc4c7c335eb887fa078dd481278dae8c8a80bd5874f4826bd2adb7d
  [gbc]=f31fcf97797ecbec7f56b2dc2e29333016061a0b988fc0aae218a1d143a3faf5
  [nes]=ef9ad9980e402dc71f7b45c7b2e9e8702e6272a44e4f3ecdcde2f4db91886bb4
  [snes]=8d64a0094438576fb09f42ee616442c9f4b97f377793de26b576d88a6fbf1205
  [sms]=021becd5dc9cae6aaa4d32369bdd5479e1e2543954770c8f628ec06fa8a6f4d6
  [c64_6581]=731bfc69d48af5987b8d6a71717ce7e24fb3fd3869d65615b0ec6cc903dda741
  [c64_8580]=8b3437895560bc733cfc9ac3100205c564b2a6ffc7e35e34083750b9ea3b5eff
  [genesis]=828eee9139b29e312c3ad7d46dfc1a94af24c4225d6493626a27bc772f18244f
  [pce]=66cd0ab320a9510e7448373360e841820d35dffcbf9b8e666d88f11234c7aea6
  [atari2600]=69fc175553f28f62d50b55c4a9a8e1f3422f2d4659aed54b469576d5780e4862
  [pokey]=7957358efdf4e0e94c8357b83d80f854b2107f6f2e149a9cce4faf5c8045ca40
  [pcspeaker]=74b98eecf956bd0790d70deae90e0a362cdf58471222eb626821205bb4983d8f
  [zx_spectrum]=bf910a2a46dfcd43c365491e9b17d4a794b8496e466d1a546ea22fd9e3cee9bf
)
for chip in "${chips[@]}"; do
  output="$workdir/$chip.wav"
  request="$(printf '{"chip":"%s","bpm":140,"sampleRate":44100,"endTick":960,"vibrato":4,"filter":900,"notes":[{"voice":0,"startTick":0,"durationTick":480,"pitch":60,"velocity":100,"volume":110,"modulation":80,"pan":32,"pitchBend":9000,"pitchBendRange":2,"noteCutTicks":120,"noteDelayTicks":15,"retrigger":2,"pitchSlide":3,"volumeSlide":-2,"instrumentId":0,"instrument":"lead"}]}' "$chip")"
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
  if [[ "${golden[$chip]}" != "$fingerprint" ]]; then
    printf 'FAIL %s: golden audio mismatch expected=%s actual=%s\n' "$chip" "${golden[$chip]}" "$fingerprint" >&2
    exit 1
  fi
  printf 'PASS %s (%s bytes, peak=%s, sha256=%s)\n' "$chip" "$(wc -c < "$output")" "$peak" "$fingerprint"
done

printf 'All %s Furnace chiptune profiles rendered successfully.\n' "${#chips[@]}"
