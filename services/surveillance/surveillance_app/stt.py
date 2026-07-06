from __future__ import annotations

from datetime import datetime, timezone
from pathlib import Path
from typing import Dict


def _core():
    from . import core

    return core


def get_whisper_model():
    core = _core()
    global_model = getattr(core, "whisper_model", None)
    with core.whisper_lock:
        if global_model is None:
            global_model = core.WhisperModel(
                core.TRANSCRIBE_MODEL,
                device="cpu",
                compute_type=core.TRANSCRIBE_COMPUTE_TYPE,
                cpu_threads=max(1, core.ANALYZER_WORKERS),
            )
            core.whisper_model = global_model
        return global_model


def transcribe_audio(path: str) -> Dict[str, object]:
    core = _core()
    if not core.TRANSCRIBE_ENABLED or not Path(path).exists():
        return {"text": "", "language": "", "language_probability": 0.0, "segments": []}

    try:
        model = get_whisper_model()
        kwargs = {
            "beam_size": core.TRANSCRIBE_BEAM_SIZE,
            "condition_on_previous_text": core.TRANSCRIBE_CONDITION_ON_PREVIOUS_TEXT,
            "word_timestamps": core.TRANSCRIBE_WORD_TIMESTAMPS,
        }
        if core.TRANSCRIBE_VAD:
            kwargs["vad_filter"] = True
            kwargs["vad_parameters"] = {"min_silence_duration_ms": core.TRANSCRIBE_VAD_MIN_SILENCE_MS}
        if core.TRANSCRIBE_LANGUAGE != "auto":
            kwargs["language"] = core.TRANSCRIBE_LANGUAGE
        segments, info = model.transcribe(path, **kwargs)
        segment_items = []
        transcript_parts = []
        for segment in segments:
            text = segment.text.strip()
            if not text:
                continue
            transcript_parts.append(text)
            segment_payload = {"start": float(segment.start), "end": float(segment.end), "text": text}
            if core.TRANSCRIBE_WORD_TIMESTAMPS and getattr(segment, "words", None):
                segment_payload["words"] = [
                    {"start": float(word.start), "end": float(word.end), "word": str(word.word).strip()}
                    for word in segment.words
                    if str(word.word).strip()
                ]
            segment_items.append(segment_payload)
        transcript = " ".join(transcript_parts)
        language = str(getattr(info, "language", "") or "").strip().lower()
        language_probability = float(getattr(info, "language_probability", 0.0) or 0.0)
        return {"text": transcript.strip(), "language": language, "language_probability": language_probability, "segments": segment_items}
    except Exception as exc:
        core.set_error(f"Transcription failed: {exc}")
        return {"text": "", "language": "", "language_probability": 0.0, "segments": []}


def write_event_transcript_files(event: Dict[str, object]) -> None:
    core = _core()
    transcript = str(event.get("transcript") or "").strip()
    if not transcript:
        return
    event_dir = core.event_directory(str(event["id"]), str(event["started_at"]))
    event_dir.mkdir(parents=True, exist_ok=True)
    (event_dir / "transcript.txt").write_text(transcript, encoding="utf-8")
    (event_dir / "transcript.json").write_text(
        core.json.dumps(
            {
                "text": transcript,
                "language": event.get("transcript_language"),
                "language_probability": event.get("transcript_language_probability"),
                "segments": event.get("transcript_segments") or [],
            },
            indent=2,
            ensure_ascii=False,
        ),
        encoding="utf-8",
    )


def event_transcript(event_id: str) -> Dict[str, object]:
    core = _core()
    event = core.load_event(event_id)
    if not event:
        return {"success": False, "message": f"Event not found: {event_id}"}
    transcript = str(event.get("transcript") or "").strip()
    if not transcript:
        return {"success": False, "message": f"No transcript found for event: {event_id}"}
    return {
        "success": True,
        "event_id": event_id,
        "transcript": transcript,
        "language": str(event.get("transcript_language") or "").strip(),
        "language_probability": float(event.get("transcript_language_probability") or 0.0),
    }
