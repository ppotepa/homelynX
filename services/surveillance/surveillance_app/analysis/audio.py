from __future__ import annotations

import math
import wave


def analyze_audio(path: str) -> tuple[float, float, float]:
    with wave.open(path, "rb") as wav_file:
        frames = wav_file.readframes(wav_file.getnframes())
        sample_width = wav_file.getsampwidth()
        frame_rate = wav_file.getframerate()
        channels = wav_file.getnchannels()

    if sample_width != 2 or channels != 1 or frame_rate != 16000:
        return 0.0, 0.0, 0.0

    samples = [int.from_bytes(frames[i:i + 2], "little", signed=True) / 32768.0 for i in range(0, len(frames), 2)]
    if not samples:
        return 0.0, 0.0, 0.0

    peak = max(abs(sample) for sample in samples)
    rms = math.sqrt(sum(sample * sample for sample in samples) / len(samples))
    frame_size = int(frame_rate * 0.03)
    speech_like = 0
    total = 0
    for start in range(0, len(samples) - frame_size, frame_size):
        chunk = samples[start:start + frame_size]
        chunk_rms = math.sqrt(sum(sample * sample for sample in chunk) / len(chunk))
        zero_crossings = sum(1 for idx in range(1, len(chunk)) if (chunk[idx - 1] < 0) != (chunk[idx] < 0)) / len(chunk)
        if 0.012 <= chunk_rms <= 0.45 and 0.015 <= zero_crossings <= 0.22:
            speech_like += 1
        total += 1

    speech_ratio = speech_like / total if total else 0.0
    return speech_ratio, peak, rms
