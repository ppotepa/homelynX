from __future__ import annotations

import json
import time
from datetime import datetime, timezone
from typing import Dict, Optional

import requests


def _core():
    from . import core

    return core


def surveillance_audit_feature(kind: object) -> str:
    core = _core()
    return core.SURVEILLANCE_AUDIT_FEATURES.get(str(kind or "").strip(), "surveillance_job")


def compact_event_audit_context(event: Optional[Dict[str, object]]) -> Dict[str, object]:
    if not event:
        return {}
    core = _core()
    event = core.normalize_event(dict(event))
    return {
        "event_id": event.get("id"),
        "kind": event.get("kind"),
        "type": event.get("type"),
        "state": event.get("state"),
        "severity": event.get("severity"),
        "category": event.get("category"),
        "title": event.get("display_title"),
        "started_at": event.get("started_at"),
        "ended_at": event.get("ended_at"),
        "duration_seconds": event.get("duration_seconds"),
        "signals": event.get("signals"),
        "peak": event.get("peak"),
        "speech_ratio": event.get("speech_ratio"),
        "motion_ratio": event.get("motion_ratio"),
        "face_count": event.get("face_count"),
        "person_count": event.get("person_count"),
        "has_transcript": bool(str(event.get("transcript") or "").strip()),
        "transcript_language": event.get("transcript_language"),
        "transcript_language_probability": event.get("transcript_language_probability"),
        "has_snapshot": bool(core.resolve_event_snapshot(event)),
        "has_preview": bool(core.resolve_event_preview(event)),
        "has_clip": bool(event.get("video") or event.get("audio")),
        "summary_provider": event.get("summary_provider"),
    }


def llm_status_payload() -> Dict[str, object]:
    core = _core()
    jobs = core.job_stats()
    status = {
        "enabled": core.LLM_ENABLED,
        "summaries_enabled": core.LLM_SUMMARIES_ENABLED,
        "model": core.LLM_MODEL,
        "base_url": core.LLM_BASE_URL,
        "min_severity": core.LLM_MIN_SEVERITY,
        "queue_size": int(jobs.get("queued", 0)) + int(jobs.get("retry", 0)),
        "last_error": core.last_llm_error,
        "last_enriched_at": core.last_llm_enriched_at,
        "available": False,
    }
    if not core.LLM_ENABLED:
        return status
    try:
        response = requests.get(f"{core.LLM_BASE_URL}/api/tags", timeout=2)
        status["available"] = response.status_code == 200
    except Exception as exc:
        status["last_error"] = str(exc)
    return status


def should_enrich_with_llm(event: Dict[str, object]) -> bool:
    core = _core()
    if not core.LLM_ENABLED or not core.LLM_SUMMARIES_ENABLED:
        return False
    event = core.normalize_event(event)
    return core.SEVERITY_RANK.get(str(event.get("severity") or "low"), 0) >= core.SEVERITY_RANK.get(core.LLM_MIN_SEVERITY, 50)


def parse_llm_json(raw_text: str) -> Dict[str, str]:
    text = raw_text.strip()
    if "```" in text:
        text = text.replace("```json", "```").split("```")[1].strip()
    start = text.find("{")
    end = text.rfind("}")
    if start >= 0 and end > start:
        text = text[start:end + 1]
    payload = json.loads(text)
    return {
        "title": str(payload.get("title") or "").strip()[:120],
        "summary": str(payload.get("summary") or "").strip()[:600],
        "notification": str(payload.get("notification") or "").strip()[:500],
    }


def audit_llm_call(
    feature: str,
    subject_type: str,
    subject_id: str,
    prompt: str,
    request_json: Dict[str, object],
    raw_response: str,
    parsed_response: object,
    status: str,
    duration_ms: float,
    error: str = "",
    metadata: Optional[Dict[str, object]] = None,
) -> None:
    core = _core()
    if not core.LLM_AUDIT_URL or not core.LLM_AUDIT_TOKEN:
        return
    try:
        requests.post(
            core.LLM_AUDIT_URL,
            json={
                "service": "surveillance",
                "feature": feature,
                "subject_type": subject_type,
                "subject_id": subject_id,
                "model": core.LLM_MODEL,
                "status": status,
                "duration_ms": round(duration_ms, 2),
                "prompt": prompt,
                "request_json": request_json,
                "raw_response": raw_response,
                "parsed_response": parsed_response,
                "error": error,
                "metadata": metadata or {},
            },
            headers={"Authorization": f"Bearer {core.LLM_AUDIT_TOKEN}"},
            timeout=2,
        )
    except Exception:
        pass


def audit_activity_call(
    feature: str,
    subject_type: str,
    subject_id: str,
    status: str,
    request_json: Dict[str, object],
    parsed_response: object,
    duration_ms: float,
    error: str = "",
    metadata: Optional[Dict[str, object]] = None,
) -> None:
    core = _core()
    if not core.LLM_AUDIT_URL or not core.LLM_AUDIT_TOKEN:
        return
    try:
        requests.post(
            core.LLM_AUDIT_URL,
            json={
                "service": "surveillance",
                "feature": feature,
                "subject_type": subject_type,
                "subject_id": subject_id,
                "model": "",
                "status": status,
                "duration_ms": round(duration_ms, 2),
                "prompt": feature,
                "request_json": request_json,
                "raw_response": "",
                "parsed_response": parsed_response,
                "error": error,
                "metadata": metadata or {},
            },
            headers={"Authorization": f"Bearer {core.LLM_AUDIT_TOKEN}"},
            timeout=2,
        )
    except Exception:
        pass


def call_llm_event_summary(event: Dict[str, object]) -> Dict[str, str]:
    core = _core()
    transcript = str(event.get("transcript") or "").strip()[:core.LLM_MAX_TRANSCRIPT_CHARS]
    payload = {
        "event_id": event.get("id"),
        "kind": event.get("kind"),
        "type": event.get("type"),
        "signals": event.get("signals"),
        "severity": event.get("severity"),
        "category": event.get("category"),
        "duration": event.get("duration_label"),
        "peak": round(float(event.get("peak", 0.0)), 2),
        "speech_ratio": round(float(event.get("speech_ratio", 0.0)), 2),
        "motion_ratio": round(float(event.get("motion_ratio", 0.0)), 3),
        "faces": int(event.get("face_count", 0)),
        "persons": int(event.get("person_count", 0)),
        "scene": str(event.get("scene_description") or "")[:500],
        "transcript": transcript,
    }
    prompt = (
        "/no_think\n"
        "You summarize a private home surveillance event for an operator. "
        "Return only compact JSON with keys title, summary, notification. "
        "Use English. Do not invent facts. Keep summary under two sentences. "
        "Keep notification under 160 characters.\n\n"
        f"Event JSON:\n{json.dumps(payload, ensure_ascii=False)}"
    )
    request_json = {
        "model": core.LLM_MODEL,
        "system": core.LLM_SYSTEM_PROMPT,
        "prompt": prompt,
        "stream": False,
        "format": "json",
        "options": {
            "temperature": 0,
            "num_predict": 180,
        },
    }
    started = time.monotonic()
    raw_text = ""
    try:
        response = requests.post(
            f"{core.LLM_BASE_URL}/api/generate",
            json=request_json,
            timeout=core.LLM_TIMEOUT_SECONDS,
        )
        response.raise_for_status()
        raw = response.json()
        raw_text = str(raw.get("response") or "")
        parsed = parse_llm_json(raw_text)
        audit_llm_call(
            "event_summary",
            "surveillance_event",
            str(event.get("id") or ""),
            prompt,
            request_json,
            raw_text,
            parsed,
            "success",
            (time.monotonic() - started) * 1000,
            metadata={"ollama": {key: raw.get(key) for key in ("prompt_eval_count", "eval_count", "total_duration") if key in raw}},
        )
        return parsed
    except Exception as exc:
        audit_llm_call(
            "event_summary",
            "surveillance_event",
            str(event.get("id") or ""),
            prompt,
            request_json,
            raw_text,
            {},
            "error",
            (time.monotonic() - started) * 1000,
            error=str(exc),
        )
        raise


def enrich_event_with_llm(event_id: str) -> None:
    core = _core()
    event = core.load_event(event_id)
    if not event or not should_enrich_with_llm(event):
        return
    try:
        llm = call_llm_event_summary(event)
        if llm.get("title"):
            event["display_title"] = llm["title"]
        if llm.get("summary"):
            event["llm_summary"] = llm["summary"]
            event["summary_provider"] = "llm"
        if llm.get("notification"):
            event["llm_notification"] = llm["notification"]
        event["llm_enriched_at"] = datetime.now(tz=timezone.utc).astimezone().isoformat(timespec="seconds")
        event["llm_error"] = ""
        core.save_event(core.normalize_event(event))
        core.last_llm_enriched_at = str(event["llm_enriched_at"])
        core.last_llm_error = None
    except Exception as exc:
        core.last_llm_error = str(exc)
        event["llm_error"] = str(exc)
        core.save_event(core.normalize_event(event))


def process_llm_job(job: Dict[str, object]) -> Dict[str, object]:
    core = _core()
    event_id = str(job.get("event_id") or "")
    event = core.load_event(event_id)
    if not event:
        raise RuntimeError(f"Event not found: {event_id}")
    event["llm_status"] = "running"
    core.save_event(event)
    try:
        llm = call_llm_event_summary(event)
        if llm.get("title"):
            event["display_title"] = llm["title"]
        if llm.get("summary"):
            event["llm_summary"] = llm["summary"]
            event["summary_provider"] = "llm"
        if llm.get("notification"):
            event["llm_notification"] = llm["notification"]
        event["llm_status"] = "done"
        event["llm_enriched_at"] = datetime.now(tz=timezone.utc).astimezone().isoformat(timespec="seconds")
        event["llm_error"] = ""
        core.save_event(core.normalize_event(event))
        core.last_llm_enriched_at = str(event["llm_enriched_at"])
        core.last_llm_error = None
        return {"llm_status": "done"}
    except Exception as exc:
        event["llm_status"] = "error"
        event["llm_error"] = str(exc)
        core.last_llm_error = str(exc)
        core.save_event(core.normalize_event(event))
        raise


def process_operator_summary_job(job: Dict[str, object]) -> Dict[str, object]:
    core = _core()
    scope = str(job.get("scope") or "").strip()
    payload = core.build_operator_summary_payload(target=scope)
    summary = core.call_llm_operator_summary(payload)
    return {"summary": summary}


def surveillance_summary(event_id: str) -> Dict[str, object]:
    core = _core()
    event = core.load_event(event_id)
    if not event:
        return {"success": False, "message": "Event not found"}
    summary = core.build_summary(event)
    return {"success": True, "event": event, "summary": summary}


def surveillance_llm_status() -> Dict[str, object]:
    return llm_status_payload()
