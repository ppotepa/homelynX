from __future__ import annotations

import os
import time
import uuid
from datetime import datetime
from typing import Dict

from . import core
from .core import (
    OPERATOR_SUMMARY_DEFAULT_HOURS,
    SEVERITY_RANK,
    app,
    build_clip_from_segments,
    build_digest,
    build_incidents,
    build_priority_items,
    build_stats,
    build_storage_stats,
    build_recent_clip,
    capture_live_snapshot,
    collect_incident_segments,
    collect_incident_transcript,
    event_matches_filter,
    filter_events_by_hours,
    latest_snapshot_file,
    load_event,
    load_incident,
    load_recent_events,
    normalize_event,
    enqueue_job,
    event_ready_for_notification,
    llm_status_payload,
    resolve_event_preview,
    resolve_event_snapshot,
    save_event,
    Query,
)
from .media.previews import build_event_preview_gif
from .events.summaries import finalize_incident_fields


@app.get("/health")
def health() -> dict[str, object]:
    from .core import RECORD_AUDIO, RECORD_VIDEO, recorder_state, recorder_restarts, last_recorder_restart_at, last_recorder_error, CAMERA_DEVICE, AUDIO_DEVICE, AUDIO_FILTER, SEGMENT_SECONDS, RETENTION_HOURS, EVENT_RETENTION_DAYS, INCIDENT_GAP_SECONDS, TRANSCRIBE_ENABLED, TRANSCRIBE_MODEL, TRANSCRIBE_LANGUAGE, TRANSCRIBE_BEAM_SIZE, TRANSCRIBE_VAD, TRANSCRIBE_WORD_TIMESTAMPS, PREVIEW_GIF_ENABLED, PREVIEW_GIF_SPEED, NOTIFY_ENABLED, TELEGRAM_BOT_TOKEN, TELEGRAM_NOTIFICATION_CHAT_ID, TELEGRAM_MIN_SEVERITY, MOTION_ENABLED, FACE_ENABLED, PERSON_ENABLED, last_segment_at, snapshot_cache, snapshot_cache_bytes, SNAPSHOT_RAM_LIMIT_BYTES, NOTIFY_MIN_INTERVAL_SECONDS, NOTIFY_BACKLOG_SUPPRESS_SECONDS, events, active_event, last_error
    last_segment_age = None
    if last_segment_at:
        try:
            last_segment_age = max(0, round(time.time() - datetime.fromisoformat(last_segment_at).timestamp()))
        except Exception:
            last_segment_age = None
    return {
        "ok": True,
        "record_audio": RECORD_AUDIO,
        "record_video": RECORD_VIDEO,
        "recorder_mode": "ffmpeg-segmenter",
        "recorder_state": recorder_state,
        "recorder_restarts": recorder_restarts,
        "last_recorder_restart_at": last_recorder_restart_at,
        "last_recorder_error": last_recorder_error,
        "camera_device": CAMERA_DEVICE,
        "audio_device": AUDIO_DEVICE,
        "audio_filter": AUDIO_FILTER,
        "segment_seconds": SEGMENT_SECONDS,
        "retention_hours": RETENTION_HOURS,
        "event_retention_days": EVENT_RETENTION_DAYS,
        "incident_gap_seconds": INCIDENT_GAP_SECONDS,
        "transcribe_enabled": TRANSCRIBE_ENABLED,
        "transcribe_model": TRANSCRIBE_MODEL,
        "transcribe_language": TRANSCRIBE_LANGUAGE,
        "transcribe_beam_size": TRANSCRIBE_BEAM_SIZE,
        "transcribe_vad": TRANSCRIBE_VAD,
        "transcribe_word_timestamps": TRANSCRIBE_WORD_TIMESTAMPS,
        "jobs": core.job_stats(),
        "preview_gif_enabled": PREVIEW_GIF_ENABLED,
        "preview_gif_speed": PREVIEW_GIF_SPEED,
        "operator_summary_default_hours": OPERATOR_SUMMARY_DEFAULT_HOURS,
        "telegram_notify_enabled": NOTIFY_ENABLED,
        "telegram_notify_configured": bool(TELEGRAM_BOT_TOKEN and TELEGRAM_NOTIFICATION_CHAT_ID),
        "telegram_notify_separate_bot": bool(os.getenv("SURV_TELEGRAM_BOT_TOKEN", "").strip()),
        "telegram_min_severity": TELEGRAM_MIN_SEVERITY,
        "motion_enabled": MOTION_ENABLED,
        "face_enabled": FACE_ENABLED,
        "person_enabled": PERSON_ENABLED,
        "llm": llm_status_payload(),
        "last_segment_at": last_segment_at,
        "last_segment_age_seconds": last_segment_age,
        "snapshot_cache_items": len(snapshot_cache),
        "snapshot_cache_bytes": snapshot_cache_bytes,
        "snapshot_cache_limit_bytes": SNAPSHOT_RAM_LIMIT_BYTES,
        "notify_min_interval_seconds": NOTIFY_MIN_INTERVAL_SECONDS,
        "notify_backlog_suppress_seconds": NOTIFY_BACKLOG_SUPPRESS_SECONDS,
        "events": len(events),
        "active_event": bool(active_event),
        "last_error": last_error,
        "supported_event_types": ["PERSON_DETECTION", "FACE_DETECTION", "DEVICE_ERROR", "DEVICE_RECOVERED"],
        "supported_severities": list(SEVERITY_RANK.keys()),
        "supported_categories": ["voice activity", "human presence", "sound anomaly", "movement", "device health", "activity"],
    }


@app.get("/events")
def list_events(
    limit: int = Query(20, ge=1, le=200),
    event_type: str = Query("", alias="type"),
    hours: int = Query(0, ge=0, le=720),
) -> dict[str, object]:
    items = load_recent_events(limit)
    items = filter_events_by_hours(items, hours)
    if event_type:
        items = [event for event in items if event_matches_filter(event, event_type)]
    return {"events": items[:limit]}


@app.get("/incidents")
def list_incidents(
    limit: int = Query(50, ge=1, le=200),
    event_type: str = Query("", alias="type"),
    hours: int = Query(0, ge=0, le=720),
) -> dict[str, object]:
    incidents = build_incidents(filter_events_by_hours(load_recent_events(max(limit * 4, 50)), hours))
    if event_type:
        filtered = []
        for incident in incidents:
            incident_events = [
                event for event in incident.get("events", [])
                if event_matches_filter(event, event_type)
            ]
            if incident_events:
                incident_copy = dict(incident)
                filtered.append(finalize_incident_fields(incident_copy, incident_events))
        incidents = filtered
    return {"incidents": incidents[:limit]}


@app.get("/priority")
def surveillance_priority(
    limit: int = Query(10, ge=1, le=100),
    event_type: str = Query("", alias="type"),
    hours: int = Query(24, ge=0, le=720),
) -> Dict[str, object]:
    items = filter_events_by_hours(load_recent_events(max(limit * 8, 100)), hours)
    if event_type:
        items = [event for event in items if event_matches_filter(event, event_type)]
    return {"success": True, "incidents": build_priority_items(items, limit)}


@app.get("/digest")
def surveillance_digest(
    hours: int = Query(24, ge=0, le=720),
    limit: int = Query(200, ge=1, le=500),
) -> Dict[str, object]:
    items = filter_events_by_hours(load_recent_events(limit), hours)
    return build_digest(items, hours)


@app.get("/summary")
def surveillance_summary(
    chat_id: str = Query(""),
    hours: int = Query(OPERATOR_SUMMARY_DEFAULT_HOURS, ge=1, le=720),
    target: str = Query(""),
) -> Dict[str, object]:
    job_id = enqueue_job(
        "operator_summary",
        target.strip() or f"summary-{uuid.uuid4().hex[:8]}",
        90,
        payload={"chat_id": chat_id, "hours": hours, "target": target.strip()},
    )
    return {
        "success": bool(job_id),
        "job_id": job_id,
        "message": "AI summary queued." if job_id else "AI summary is already queued.",
        "hours": hours,
        "target": target,
    }


@app.get("/llm/status")
def surveillance_llm_status() -> Dict[str, object]:
    return {"success": True, "llm": llm_status_payload()}


@app.get("/incidents/{incident_id}")
def incident_details(incident_id: str) -> Dict[str, object]:
    incident = load_incident(incident_id)
    if not incident:
        return {"success": False, "message": f"Incident not found: {incident_id}"}
    return {"success": True, "incident": incident}


@app.get("/incidents/{incident_id}/snapshot")
def incident_snapshot(incident_id: str) -> Dict[str, object]:
    incident = load_incident(incident_id)
    if not incident:
        return {"success": False, "message": f"Incident not found: {incident_id}"}
    for event in incident.get("events", []):
        snapshot = resolve_event_snapshot(event)
        if snapshot:
            return {"success": True, "file": snapshot, "incident_id": incident_id, "type": "image"}
    return {"success": False, "message": f"No snapshot found for incident: {incident_id}"}


@app.get("/incidents/{incident_id}/transcript")
def incident_transcript(incident_id: str) -> Dict[str, object]:
    incident = load_incident(incident_id)
    if not incident:
        return {"success": False, "message": f"Incident not found: {incident_id}"}
    transcript = collect_incident_transcript(incident)
    if not transcript:
        return {"success": False, "message": f"No transcript found for incident: {incident_id}"}
    languages = []
    for event in incident.get("events", []):
        language = str(event.get("transcript_language") or "").strip()
        if language:
            languages.append({
                "event_id": event.get("id"),
                "language": language,
                "language_probability": float(event.get("transcript_language_probability") or 0.0),
            })
    return {"success": True, "incident_id": incident_id, "transcript": transcript, "languages": languages}


@app.get("/stats")
def surveillance_stats(
    limit: int = Query(100, ge=1, le=500),
    hours: int = Query(0, ge=0, le=720),
) -> Dict[str, object]:
    items = filter_events_by_hours(load_recent_events(limit), hours)
    return {"success": True, "stats": build_stats(items)}


@app.get("/storage")
def surveillance_storage() -> Dict[str, object]:
    return {"success": True, "storage": build_storage_stats()}


@app.get("/incidents/{incident_id}/clip")
def incident_clip(incident_id: str) -> Dict[str, object]:
    incident = load_incident(incident_id)
    if not incident:
        return {"success": False, "message": f"Incident not found: {incident_id}"}

    segments = collect_incident_segments(incident)
    if not segments:
        return {"success": False, "message": f"No segments found for incident: {incident_id}"}

    result = build_clip_from_segments(segments, incident_id)
    if result.get("success"):
        result["event_count"] = int(incident.get("event_count") or 0)
        result["event_types"] = incident.get("event_types") or []
        result["incident_summary"] = incident.get("summary") or ""
    return result


@app.get("/events/{event_id}/clip")
def event_clip(event_id: str) -> Dict[str, object]:
    event = load_event(event_id)
    if not event:
        return {"success": False, "message": f"Event not found: {event_id}"}

    return build_clip_from_segments(event.get("segments", []), event_id)


@app.get("/events/{event_id}")
def event_details(event_id: str) -> Dict[str, object]:
    event = load_event(event_id)
    if not event:
        return {"success": False, "message": f"Event not found: {event_id}"}
    return {"success": True, "event": event}


@app.get("/events/{event_id}/snapshot")
def event_snapshot(event_id: str) -> Dict[str, object]:
    event = load_event(event_id)
    if not event:
        return {"success": False, "message": f"Event not found: {event_id}"}
    snapshot = resolve_event_snapshot(event)
    if not snapshot:
        return {"success": False, "message": f"No snapshot found for event: {event_id}"}
    return {"success": True, "file": snapshot, "event_id": event_id, "type": "image"}


@app.get("/events/{event_id}/preview")
def event_preview(event_id: str) -> Dict[str, object]:
    event = load_event(event_id)
    if not event:
        return {"success": False, "message": f"Event not found: {event_id}"}
    preview = resolve_event_preview(event)
    if preview:
        return {"success": True, "file": preview, "event_id": event_id, "type": "animation"}
    result = build_event_preview_gif(event)
    if result.get("success"):
        event["preview_gif"] = str(result.get("file") or "")
        event["preview_status"] = "done"
        save_event(normalize_event(event))
        result["event_id"] = event_id
        return result
    return {"success": False, "message": str(result.get("message") or f"No preview found for event: {event_id}")}


@app.get("/events/{event_id}/transcript")
def event_transcript(event_id: str) -> Dict[str, object]:
    event = load_event(event_id)
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


@app.get("/clip")
def recent_clip(seconds: int = 30) -> Dict[str, object]:
    return build_recent_clip(seconds)


@app.get("/snapshot")
def recent_snapshot() -> Dict[str, object]:
    snapshot = latest_snapshot_file()
    if not snapshot:
        return {"success": False, "message": "No recent snapshot is available yet."}
    return {"success": True, "file": snapshot, "type": "image"}


@app.get("/snapshot/live")
def live_snapshot() -> Dict[str, object]:
    snapshot = capture_live_snapshot()
    if not snapshot:
        return {"success": False, "message": "Could not capture a live snapshot."}
    return {"success": True, "file": snapshot, "type": "image"}

