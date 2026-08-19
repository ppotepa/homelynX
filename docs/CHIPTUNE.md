# Chiptune composer

`/chiptune` is deterministic: the same normalized options and seed produce the same score. The score is built at 960 PPQ, assigned to the channel limits of the selected platform, and rendered by a pinned headless Furnace engine in the production image.

## Inputs

Exactly one source is required:

```text
/chiptune notes="C4/8 E4/8 G4/4" chip=gameboy
/chiptune degrees="1/8 b3/8 5/8 8/4" key=D scale=minor chip=nes
/chiptune generate=scale key=C scale=major direction=updown
/chiptune generate=arp key=A scale=harmonic_minor chip=snes
/chiptune generate=riff key=E scale=phrygian style=boss seed=42 chip=sms
/chiptune format=mp3 tempo_mode=file  # with an attached MIDI file
```

Supported chips are `gameboy`, `nes`, `snes` and `sms` (`sega` is an alias for `sms`). Supported output formats are WAV, MP3, OGG and FLAC. `range=C3:C6` overrides `octave` and `octaves`; transposition is applied afterwards.

MIDI type 0 and type 1 tracks are merged by absolute tick. Tempo events form a complete tempo map. `tempo_mode=override bpm=160` replaces file tempo, while `quantize=1/4|1/8|1/16` selects the hardware score grid.

## Telegram composer

After sending audio, Telegram offers octave, semitone, BPM, variation, instrument, chip and repeat controls. The normalized specification is retained in SQLite for seven days and callbacks are bound to the originating user and chat. There is no background session poller.

## Runtime limits

- 5 MB MIDI input
- 4096 notes
- 120 seconds of audio
- one render at a time per bot process
- 60-second renderer timeout by default (`TORRENTBOT_CHIPTUNE_RENDER_TIMEOUT_SECONDS`)

Production uses `CHIPTUNE_RENDERER_PATH=/usr/local/bin/homelynx-chiptune-renderer`. If the configured helper is missing or fails, the command returns a diagnostic error and does not silently claim hardware fidelity.
