from __future__ import annotations

import json
import time
from datetime import datetime, timezone
from typing import Dict


def _core():
    from .. import core

    return core


def process_stt_job(job: Dict[str, object]) -> Dict[str, object]:
    core = _core()
    event_id = str(job.get("event_id") or "")
    event = core.load_event(event_id)
    if not event:
        raise RuntimeError(f"Event not found: {event_id}")
    if not event.get("audio"):
        event["transcript_status"] = "skipped"
        event["llm_status"] = "queued" if core.should_enrich_with_llm(event) else "skipped"
        core.save_event(event)
        if event["llm_status"] == "queued":
            core.enqueue_job("llm_summary", event_id, int(event.get("priority_score") or 50))
        elif core.event_ready_for_notification(event):
            core.enqueue_job("send_notification", event_id, int(event.get("priority_score") or 50) + 5)
        return {"transcript_status": "skipped"}

    event["transcript_status"] = "running"
    core.save_event(event)
    transcript_result = core.transcribe_audio(str(event.get("audio") or ""))
    event["transcript"] = str(transcript_result.get("text") or "").strip()
    event["transcript_language"] = str(transcript_result.get("language") or "").strip().lower()
    event["transcript_language_probability"] = float(transcript_result.get("language_probability") or 0.0)
    event["transcript_segments"] = transcript_result.get("segments") or []
    event["transcript_status"] = "done" if event.get("transcript") else "empty"
    event["scene_description"] = core.build_scene_description(event)
    event["summary"] = core.build_summary(event)
    event["summary_provider"] = "rules"
    event = core.normalize_event(event)
    core.write_event_transcript_files(event)
    core.save_event(event)

    if core.should_enrich_with_llm(event):
        event["llm_status"] = "queued"
        core.save_event(event)
        core.enqueue_job("llm_summary", event_id, int(event.get("priority_score") or 50))
    elif core.event_ready_for_notification(event):
        core.enqueue_job("send_notification", event_id, int(event.get("priority_score") or 50) + 5)

    return {
        "transcript_status": event.get("transcript_status"),
        "language": event.get("transcript_language"),
        "language_probability": event.get("transcript_language_probability"),
        "chars": len(str(event.get("transcript") or "")),
    }


def process_preview_gif_job(job: Dict[str, object]) -> Dict[str, object]:
    core = _core()
    event_id = str(job.get("event_id") or "")
    event = core.load_event(event_id)
    if not event:
        raise RuntimeError(f"Event not found: {event_id}")
    event["preview_status"] = "running"
    core.save_event(event)
    result = core.build_event_preview_gif(event)
    if not result.get("success"):
        event["preview_status"] = "failed"
        event["preview_error"] = str(result.get("message") or "Preview GIF failed")[:1000]
        core.save_event(core.normalize_event(event))
        if core.event_ready_for_notification(event):
            core.enqueue_job("send_notification", event_id, int(event.get("priority_score") or 50) + 5)
        return {"preview_status": "failed", "error": event["preview_error"]}
    event["preview_gif"] = str(result.get("file") or "")
    event["preview_status"] = "done"
    event["preview_source_segment_id"] = result.get("source_segment_id")
    event["preview_generated_at"] = datetime.now(tz=timezone.utc).astimezone().isoformat(timespec="seconds")
    core.save_event(core.normalize_event(event))
    if core.event_ready_for_notification(event):
        core.enqueue_job("send_notification", event_id, int(event.get("priority_score") or 50) + 5)
    return {"preview_status": "done", "file": event["preview_gif"]}


def process_send_notification_job(job: Dict[str, object]) -> Dict[str, object]:
    core = _core()
    event_id = str(job.get("event_id") or "")
    event = core.load_event(event_id)
    if not event:
        raise RuntimeError(f"Event not found: {event_id}")
    if str(event.get("notification_status") or "") == "sent":
        return {"notification_status": "already_sent"}
    if not core.event_ready_for_notification(event):
        raise RuntimeError("Notification dependencies are not finished yet")
    event["notification_status"] = "sending"
    core.save_event(event)
    sent = core.notify_telegram_preview(event)
    if not sent:
        core.notify_telegram(event)
        sent = True
    event = core.load_event(event_id) or event
    event["notification_status"] = "sent" if sent else "skipped"
    event["notification_sent_at"] = datetime.now(tz=timezone.utc).astimezone().isoformat(timespec="seconds") if sent else ""
    core.save_event(core.normalize_event(event))
    return {"notification_status": event["notification_status"]}


def process_operator_summary_job(job: Dict[str, object]) -> Dict[str, object]:
    core = _core()
    payload = json.loads(str(job.get("payload_json") or "{}"))
    chat_id = str(payload.get("chat_id") or core.TELEGRAM_NOTIFICATION_CHAT_ID or "")
    hours = int(payload.get("hours") or core.OPERATOR_SUMMARY_DEFAULT_HOURS)
    target = str(payload.get("target") or "").strip()
    summary_payload = core.build_operator_summary_payload(hours=hours, target=target)
    report = core.call_llm_operator_summary(summary_payload)
    title = f"AI surveillance summary\nScope: {summary_payload.get('scope')}"
    core.send_telegram_text_chunks(chat_id, title, report)
    return {"summary_status": "done", "scope": summary_payload.get("scope"), "chars": len(report)}


def process_job(job: Dict[str, object]) -> Dict[str, object]:
    core = _core()
    kind = str(job.get("kind") or "")
    if kind == "stt":
        return process_stt_job(job)
    if kind == "llm_summary":
        return core.process_llm_job(job)
    if kind == "preview_gif":
        return process_preview_gif_job(job)
    if kind == "send_notification":
        return process_send_notification_job(job)
    if kind == "operator_summary":
        return process_operator_summary_job(job)
    raise RuntimeError(f"Unsupported job kind: {kind}")


def job_worker(worker_index: int) -> None:
    core = _core()
    while not core.stop_event.is_set():
        job = core.claim_next_job()
        if not job:
            core.stop_event.wait(core.JOB_POLL_SECONDS)
            continue
        started_at = time.monotonic()
        kind = str(job.get("kind") or "")
        event_id = str(job.get("event_id") or "")
        feature = core.surveillance_audit_feature(kind)
        event_context = core.compact_event_audit_context(core.load_event(event_id)) if event_id else {}
        try:
            result = process_job(job)
            core.complete_job(str(job["id"]), result)
            core.audit_activity_call(
                feature=feature,
                subject_type=feature,
                subject_id=event_id or str(job.get("id") or ""),
                status="success",
                request_json={
                    "job_id": job.get("id"),
                    "kind": kind,
                    "event_id": event_id,
                    "attempts": job.get("attempts"),
                    "max_attempts": job.get("max_attempts"),
                    "priority": job.get("priority"),
                    "event": event_context,
                },
                parsed_response=result,
                duration_ms=(time.monotonic() - started_at) * 1000,
                metadata={"worker_index": worker_index},
            )
        except Exception as exc:
            core.set_error(f"Job {kind} failed for event {event_id}: {exc}")
            core.fail_job(job, str(exc))
            attempts = int(job.get("attempts") or 0)
            max_attempts = int(job.get("max_attempts") or core.JOB_MAX_ATTEMPTS)
            core.audit_activity_call(
                feature=feature,
                subject_type=feature,
                subject_id=event_id or str(job.get("id") or ""),
                status="error",
                request_json={
                    "job_id": job.get("id"),
                    "kind": kind,
                    "event_id": event_id,
                    "attempts": attempts,
                    "max_attempts": max_attempts,
                    "error": str(exc),
                    "event": event_context,
                },
                parsed_response={"error": str(exc)},
                duration_ms=(time.monotonic() - started_at) * 1000,
                error=str(exc),
                metadata={"worker_index": worker_index},
            )
