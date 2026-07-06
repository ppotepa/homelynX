from __future__ import annotations

from dataclasses import asdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict


def _core():
    from .. import core

    return core


def create_event(result) -> Dict[str, object]:
    core = _core()
    event_stamp, _, started_at = core.event_start_tuple()
    event_id = f"evt-{event_stamp}-{core.uuid.uuid4().hex[:6]}"
    signals = core.result_signals(result)
    kind = core.kind_from_signals(signals)
    event = {
        "id": event_id,
        "state": "open",
        "kind": kind,
        "type": core.KIND_TYPE_MAP.get(kind, "activity"),
        "signals": signals,
        "event_types": sorted([name.upper() for name, enabled in signals.items() if enabled]),
        "started_at": started_at,
        "started_at_ts": result.segment.timestamp,
        "updated_at": result.segment.started_at,
        "updated_at_ts": result.segment.timestamp,
        "ended_at": None,
        "ended_at_ts": None,
        "duration_seconds": 0,
        "segments": [],
        "analysis_segments": [],
        "representative_score": 0.0,
        "representative_segment_id": None,
        "representative_at": None,
        "representative_at_ts": None,
        "representative_video": None,
        "representative_audio": None,
        "representative_snapshot": None,
        "representative_annotated_snapshot": None,
        "speech_ratio": result.speech_ratio,
        "peak": result.peak,
        "rms": result.rms,
        "motion_ratio": result.motion_ratio,
        "face_count": result.face_count,
        "person_count": result.person_count,
        "snapshot": None,
        "annotated_snapshot": result.annotated_snapshot_path,
        "transcript": "",
        "transcript_language": "",
        "transcript_language_probability": 0.0,
        "summary": "",
        "video": None,
        "audio": None,
        "preview_gif": None,
        "preview_status": "pending",
        "notification_status": "pending",
        "finalized_at": None,
    }
    core.apply_representative_result(event, result)
    event["segments"] = core.get_recent_segments_for_event(result.segment.timestamp, result.segment.timestamp)
    core.save_event(event)
    return event


def should_transcribe_event(event: Dict[str, object]) -> bool:
    core = _core()
    if not core.TRANSCRIBE_ENABLED or not event.get("audio"):
        return False
    event = core.normalize_event(event)
    signals = core.normalize_signals(event)
    kind = str(event.get("kind") or "")
    severity = str(event.get("severity") or "low")
    if kind == "VOICE" or signals.get("voice"):
        return True
    if float(event.get("speech_ratio", 0.0)) > 0:
        return True
    return core.SEVERITY_RANK.get(severity, 0) >= core.SEVERITY_RANK["high"]


def prepare_event_processing(event: Dict[str, object]) -> Dict[str, object]:
    core = _core()
    event = core.normalize_event(event)
    if should_transcribe_event(event):
        event["transcript_status"] = "queued"
        event["llm_status"] = "waiting_for_transcript" if core.should_enrich_with_llm(event) else "skipped"
    else:
        event["transcript_status"] = "skipped"
        event["llm_status"] = "queued" if core.should_enrich_with_llm(event) else "skipped"
    return event


def enqueue_event_preview_notification(event: Dict[str, object]) -> None:
    core = _core()
    if not core.should_build_preview_gif(event):
        return
    event["preview_status"] = "queued"
    core.save_event(event)
    core.enqueue_job("preview_gif", str(event["id"]), int(event.get("priority_score") or 50) + 50)


def enqueue_event_processing(event: Dict[str, object]) -> None:
    core = _core()
    event = core.normalize_event(event)
    priority = int(event.get("priority_score") or 50)
    event_id = str(event.get("id") or "")
    if not event_id:
        return
    enqueue_event_preview_notification(event)
    if str(event.get("transcript_status") or "") == "queued":
        core.enqueue_job("stt", event_id, priority + 20)
        return
    if str(event.get("llm_status") or "") == "queued":
        core.enqueue_job("llm_summary", event_id, priority)
        return
    if not core.should_build_preview_gif(event):
        event["preview_status"] = "skipped"
        core.save_event(event)
        core.enqueue_job("send_notification", event_id, priority + 5)


def update_event(event: Dict[str, object], result) -> None:
    core = _core()
    event["updated_at"] = result.segment.started_at
    event["updated_at_ts"] = result.segment.timestamp
    event["segments"].append(asdict(result.segment))
    event["speech_ratio"] = max(float(event.get("speech_ratio", 0.0)), result.speech_ratio)
    event["peak"] = max(float(event.get("peak", 0.0)), result.peak)
    event["rms"] = max(float(event.get("rms", 0.0)), result.rms)
    event["motion_ratio"] = max(float(event.get("motion_ratio", 0.0)), result.motion_ratio)
    event["face_count"] = max(int(event.get("face_count", 0)), result.face_count)
    event["person_count"] = max(int(event.get("person_count", 0)), result.person_count)
    if result.annotated_snapshot_path:
        event["annotated_snapshot"] = result.annotated_snapshot_path
    core.apply_representative_result(event, result)
    core.merge_event_signals(event, core.result_signals(result))
    if result.noise_spike:
        event["signals"]["loud"] = True
    event["kind"] = max(
        [str(event.get("kind") or "ACTIVITY"), core.kind_from_signals(event["signals"])],
        key=lambda value: core.EVENT_KIND_PRIORITY.get(value, 0),
    )
    event["type"] = core.event_type_for_event(event)
    core.save_event(event)


def finalize_event(event: Dict[str, object], end_ts: float) -> Dict[str, object]:
    """Finalize an event into a durable archive with media and metadata."""
    core = _core()
    finished_at = datetime.fromtimestamp(end_ts, tz=timezone.utc).astimezone().isoformat(timespec="seconds")
    event["state"] = "finalized"
    event["ended_at"] = finished_at
    event["ended_at_ts"] = end_ts
    event["updated_at"] = finished_at
    event["updated_at_ts"] = end_ts
    event["duration_seconds"] = max(core.SEGMENT_SECONDS, round(end_ts - float(event.get("started_at_ts") or end_ts)))
    event["segments"] = core.get_recent_segments_for_event(float(event.get("started_at_ts") or end_ts), end_ts)

    event_dir = core.event_directory(str(event["id"]), str(event["started_at"]))
    event_dir.mkdir(parents=True, exist_ok=True)

    snapshot_path = event_dir / "snapshot.jpg"
    if core.extract_event_snapshot_from_segments(event, snapshot_path):
        event["snapshot"] = str(snapshot_path)

    clip_result = core.build_clip_from_segments(event.get("segments", []), str(event["id"]))
    if clip_result.get("success"):
        clip_path = Path(str(clip_result["file"]))
        if clip_result.get("type") == "video":
            final_video = event_dir / "event.mp4"
            core.shutil.copy2(clip_path, final_video)
            event["video"] = str(final_video)
        if clip_result.get("type") in {"audio", "video"}:
            audio_candidates = list(clip_path.parent.glob("*audio.wav"))
            if audio_candidates:
                final_audio = event_dir / "event.wav"
                core.shutil.copy2(audio_candidates[0], final_audio)
                event["audio"] = str(final_audio)

    if not event.get("audio"):
        audio_only = core.build_clip_from_segments(event.get("segments", []), f"{event['id']}-audio")
        if audio_only.get("success") and audio_only.get("type") == "audio":
            final_audio = event_dir / "event.wav"
            core.shutil.copy2(Path(str(audio_only["file"])), final_audio)
            event["audio"] = str(final_audio)

    event = core.normalize_event(event)

    event["transcript"] = str(event.get("transcript") or "").strip()
    event["transcript_language"] = str(event.get("transcript_language") or "").strip().lower()
    event["transcript_language_probability"] = float(event.get("transcript_language_probability") or 0.0)
    event["transcript_segments"] = event.get("transcript_segments") or []
    event["scene_description"] = core.build_scene_description(event)
    event["summary"] = core.build_summary(event)
    event["summary_provider"] = "rules"
    event["finalized_at"] = datetime.now(tz=timezone.utc).astimezone().isoformat(timespec="seconds")
    event = prepare_event_processing(event)

    if event.get("transcript"):
        core.write_event_transcript_files(event)
    if event.get("scene_description"):
        (event_dir / "scene.txt").write_text(str(event["scene_description"]), encoding="utf-8")
    (event_dir / "summary.txt").write_text(str(event["summary"]), encoding="utf-8")
    core.save_event(event)
    core.audit_activity_call(
        feature="surveillance_event",
        subject_type="surveillance_event",
        subject_id=str(event.get("id") or ""),
        status="finalized",
        request_json=core.compact_event_audit_context(event),
        parsed_response={
            "processing": {
                "transcript_status": event.get("transcript_status"),
                "llm_status": event.get("llm_status"),
                "preview_status": event.get("preview_status"),
            },
            "media": {
                "audio": bool(event.get("audio")),
                "video": bool(event.get("video")),
                "snapshot": bool(event.get("snapshot")),
            },
        },
        duration_ms=0,
    )
    enqueue_event_processing(event)
    return event
