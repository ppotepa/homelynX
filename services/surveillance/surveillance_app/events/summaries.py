from __future__ import annotations

import json
import time
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Optional

from .classification import (
    SEVERITY_RANK,
    duration_label,
    normalize_event,
    normalize_event_kind,
)


def build_summary(event: Dict[str, object]) -> str:
    from ..core import normalize_signals

    event = normalize_event(event)
    kind = str(event.get("kind") or "ACTIVITY")
    signals = normalize_signals(event)
    duration = int(event.get("duration_seconds") or 0)
    transcript = str(event.get("transcript") or "").strip()
    peak = float(event.get("peak", 0.0))
    speech_ratio = float(event.get("speech_ratio", 0.0))
    motion_ratio = float(event.get("motion_ratio", 0.0))
    face_count = int(event.get("face_count", 0))
    person_count = int(event.get("person_count", 0))

    if kind == "PERSON_DETECTION":
        prefix = f"Person detected for about {duration}s."
    elif kind == "FACE_DETECTION":
        prefix = f"Face detected for about {duration}s."
    elif kind == "VOICE" and signals.get("loud"):
        prefix = f"Voice and loud sound detected for about {duration}s."
    elif kind == "VOICE":
        prefix = f"Voice detected for about {duration}s."
    elif kind == "LOUD":
        prefix = f"Loud sound detected for about {duration}s."
    elif kind == "MOVEMENT":
        prefix = f"Movement detected from start to end for about {duration}s."
    elif kind == "DEVICE_ERROR":
        prefix = "Surveillance recorder reported a device error."
    elif kind == "DEVICE_RECOVERED":
        prefix = "Surveillance recorder recovered after a device error."
    else:
        prefix = f"{kind.replace('_', ' ').title()} event detected for about {duration}s."

    if kind in {"DEVICE_ERROR", "DEVICE_RECOVERED"}:
        if transcript:
            return f"{prefix} Details: {transcript}"
        return prefix

    active_signals = ", ".join(name for name, enabled in signals.items() if enabled) or "activity"
    metrics = f" Signals={active_signals}. Peak={peak:.2f}, speech_ratio={speech_ratio:.2f}, motion_ratio={motion_ratio:.3f}, faces={face_count}, persons={person_count}."
    if transcript:
        return f"{prefix}{metrics} Transcript: {transcript}"

    return f"{prefix}{metrics} No speech transcript available."


def build_scene_description(event: Dict[str, object]) -> str:
    from ..core import normalize_signals

    event = normalize_event(event)
    kind = str(event.get("kind") or "ACTIVITY")
    signals = normalize_signals(event)
    motion_ratio = float(event.get("motion_ratio", 0.0))
    face_count = int(event.get("face_count", 0))
    person_count = int(event.get("person_count", 0))
    transcript = str(event.get("transcript") or "").strip()

    if kind == "PERSON_DETECTION":
        if face_count > 0:
            return f"Person visible in frame, likely near camera. persons={person_count}, faces={face_count}"
        return f"Person visible in frame. persons={person_count}, motion={motion_ratio:.3f}"
    if kind == "FACE_DETECTION":
        return f"Face visible near camera. faces={face_count}, motion={motion_ratio:.3f}"
    if kind == "MOVEMENT":
        return f"Movement start/end captured without a confirmed person. motion={motion_ratio:.3f}"
    if kind == "VOICE" and signals.get("loud"):
        return "Speech and strong audio activity detected in the same window."
    if kind == "VOICE":
        return "Short speech activity detected."
    if kind == "LOUD":
        return "Sustained loud audio detected."
    if kind == "DEVICE_ERROR":
        return "Recorder stopped producing valid data and needs attention."
    if kind == "DEVICE_RECOVERED":
        return "Recorder resumed normal operation."
    if transcript:
        return f"Activity detected with transcript: {transcript[:120]}"
    return "General activity detected."


def build_event_display_summary(event: Dict[str, object]) -> str:
    llm_summary = str(event.get("llm_summary") or "").strip()
    if llm_summary:
        return llm_summary
    return str(event.get("summary") or build_summary(event)).strip()


def finalize_incident_fields(incident: Dict[str, object], incident_events: List[Dict[str, object]]) -> Dict[str, object]:
    normalized_events = [normalize_event(event) for event in incident_events]
    incident["events"] = normalized_events
    incident["event_count"] = len(normalized_events)
    incident["event_types"] = sorted({str(item.get("type") or "activity") for item in normalized_events})
    incident["event_kinds"] = sorted({str(item.get("kind") or normalize_event_kind(item)) for item in normalized_events})
    incident["categories"] = sorted({str(item.get("category") or "activity") for item in normalized_events})
    severity = max(
        (str(item.get("severity") or "low") for item in normalized_events),
        key=lambda value: SEVERITY_RANK.get(value, 0),
        default="low",
    )
    incident["severity"] = severity
    incident["priority_score"] = min(100, max((int(item.get("priority_score") or 0) for item in normalized_events), default=0) + max(0, len(normalized_events) - 1) * 3)
    incident["duration_seconds"] = max(0, round(float(incident.get("ended_at_ts") or 0.0) - float(incident.get("started_at_ts") or 0.0)))
    incident["duration_label"] = duration_label(incident["duration_seconds"])
    incident["display_title"] = build_incident_title(incident)
    incident["summary"] = build_incident_summary(incident)
    incident["has_transcript"] = any(bool(event.get("has_transcript")) for event in normalized_events)
    incident["has_snapshot"] = any(bool(event.get("has_snapshot")) for event in normalized_events)
    incident["has_clip"] = any(bool(event.get("has_clip")) for event in normalized_events)
    return incident


def build_incident_title(incident: Dict[str, object]) -> str:
    kinds = set(str(item) for item in incident.get("event_kinds", []))
    if "PERSON_DETECTION" in kinds:
        return "Person detection"
    if "FACE_DETECTION" in kinds:
        return "Face detection"
    if "VOICE" in kinds:
        return "Voice activity"
    if "LOUD" in kinds:
        return "Sound anomaly"
    if "MOVEMENT" in kinds:
        return "Movement"
    if "DEVICE_ERROR" in kinds:
        return "Recorder problem"
    types = set(str(item) for item in incident.get("event_types", []))
    if "speech+loud" in types:
        return "Voice with loud sound"
    if "person_detected" in types:
        return "Human presence"
    if "face_detected" in types:
        return "Face near camera"
    if "speech_extended" in types:
        return "Extended voice activity"
    if "speech" in types:
        return "Voice activity"
    if "noise_spike" in types or "loud" in types:
        return "Sound anomaly"
    if "motion" in types:
        return "Movement"
    if "device_error" in types:
        return "Recorder problem"
    return "Surveillance activity"


def build_incident_summary(incident: Dict[str, object]) -> str:
    events_list = incident.get("events", [])
    llm_summaries = [str(event.get("llm_summary") or "").strip() for event in events_list if str(event.get("llm_summary") or "").strip()]
    if llm_summaries:
        return " ".join(llm_summaries[:2])[:500]

    event_count = int(incident.get("event_count") or 0)
    duration = str(incident.get("duration_label") or "0s")
    categories = ", ".join(incident.get("categories") or [])
    severity = str(incident.get("severity") or "low").upper()
    first_summary = ""
    for event in events_list:
        first_summary = build_event_display_summary(event)
        if first_summary:
            break
    if first_summary:
        return f"{severity} incident over {duration}. Signals: {categories or 'activity'}. {first_summary[:260]}"
    return f"{severity} incident over {duration}. Signals: {categories or 'activity'}. Events: {event_count}."


def build_incidents(event_items: List[Dict[str, object]]) -> List[Dict[str, object]]:
    from ..core import INCIDENT_GAP_SECONDS

    finalized = [
        event for event in event_items
        if str(event.get("state") or "finalized") == "finalized"
    ]
    finalized.sort(key=lambda event: float(event.get("started_at_ts") or 0.0))

    incidents: List[Dict[str, object]] = []
    current: Optional[Dict[str, object]] = None
    current_events: List[Dict[str, object]] = []

    for event in finalized:
        start_ts = float(event.get("started_at_ts") or 0.0)
        end_ts = float(event.get("ended_at_ts") or event.get("updated_at_ts") or start_ts)
        if current is None or start_ts - float(current["ended_at_ts"]) > INCIDENT_GAP_SECONDS:
            if current is not None:
                incidents.append(finalize_incident_fields(current, current_events))
            current_events = [event]
            current = {
                "id": f"inc-{str(event.get('id') or 'event')}",
                "started_at": event.get("started_at"),
                "started_at_ts": start_ts,
                "ended_at": event.get("ended_at") or event.get("updated_at"),
                "ended_at_ts": end_ts,
            }
        else:
            current_events.append(event)
            current["ended_at"] = event.get("ended_at") or event.get("updated_at")
            current["ended_at_ts"] = end_ts

    if current is not None:
        incidents.append(finalize_incident_fields(current, current_events))

    incidents.sort(key=lambda item: str(item.get("started_at") or ""), reverse=True)
    return incidents


def build_stats(event_items: List[Dict[str, object]]) -> Dict[str, object]:
    events_by_type: Dict[str, int] = {}
    events_by_kind: Dict[str, int] = {}
    events_by_severity: Dict[str, int] = {}
    events_by_category: Dict[str, int] = {}
    incidents_by_type: Dict[str, int] = {}
    incidents_by_severity: Dict[str, int] = {}
    incidents = build_incidents(event_items)

    for event in event_items:
        event = normalize_event(event)
        event_type = str(event.get("type") or "activity")
        event_kind = str(event.get("kind") or normalize_event_kind(event))
        events_by_type[event_type] = events_by_type.get(event_type, 0) + 1
        events_by_kind[event_kind] = events_by_kind.get(event_kind, 0) + 1
        severity = str(event.get("severity") or "low")
        category = str(event.get("category") or "activity")
        events_by_severity[severity] = events_by_severity.get(severity, 0) + 1
        events_by_category[category] = events_by_category.get(category, 0) + 1

    for incident in incidents:
        incident_severity = str(incident.get("severity") or "low")
        incidents_by_severity[incident_severity] = incidents_by_severity.get(incident_severity, 0) + 1
        for event_type in incident.get("event_types", []):
            key = str(event_type or "activity")
            incidents_by_type[key] = incidents_by_type.get(key, 0) + 1

    return {
        "events_total": len(event_items),
        "incidents_total": len(incidents),
        "events_by_type": dict(sorted(events_by_type.items())),
        "events_by_kind": dict(sorted(events_by_kind.items())),
        "events_by_severity": dict(sorted(events_by_severity.items())),
        "events_by_category": dict(sorted(events_by_category.items())),
        "incidents_by_type": dict(sorted(incidents_by_type.items())),
        "incidents_by_severity": dict(sorted(incidents_by_severity.items())),
        "latest_event_at": event_items[0].get("started_at") if event_items else None,
        "latest_incident_at": incidents[0].get("started_at") if incidents else None,
    }


def build_digest(event_items: List[Dict[str, object]], hours: int) -> Dict[str, object]:
    incidents = build_incidents(event_items)
    stats = build_stats(event_items)
    important = [
        incident for incident in incidents
        if SEVERITY_RANK.get(str(incident.get("severity") or "low"), 0) >= SEVERITY_RANK["high"]
    ]
    top_incidents = sorted(incidents, key=lambda item: int(item.get("priority_score") or 0), reverse=True)[:5]
    summary = (
        f"{len(incidents)} incidents and {len(event_items)} events"
        f"{f' in the last {hours}h' if hours else ''}. "
        f"Important incidents: {len(important)}."
    )
    return {
        "success": True,
        "hours": hours,
        "summary": summary,
        "stats": stats,
        "top_incidents": top_incidents,
    }


def build_operator_summary_payload(hours: int = 24, target: str = "") -> Dict[str, object]:
    from ..core import OPERATOR_SUMMARY_DEFAULT_HOURS, OPERATOR_SUMMARY_MAX_EVENTS, load_event, load_recent_events

    target = target.strip()
    if target.startswith("evt-"):
        event = load_event(target)
        return {
            "scope": f"event {target}",
            "event": event,
            "incidents": [],
            "stats": build_stats([event] if event else []),
        }
    if target.startswith("inc-"):
        from .incidents import load_incident

        incident = load_incident(target)
        events_list = incident.get("events", []) if incident else []
        return {
            "scope": f"incident {target}",
            "incident": incident,
            "events": events_list,
            "stats": build_stats(events_list),
        }

    hours = max(1, min(hours or OPERATOR_SUMMARY_DEFAULT_HOURS, 720))
    events_list = filter_events_by_hours(load_recent_events(OPERATOR_SUMMARY_MAX_EVENTS), hours)
    incidents = build_incidents(events_list)
    return {
        "scope": f"last {hours}h",
        "hours": hours,
        "stats": build_stats(events_list),
        "digest": build_digest(events_list, hours),
        "incidents": incidents[:20],
        "events": events_list[:OPERATOR_SUMMARY_MAX_EVENTS],
    }


def compact_event_for_summary(event: Dict[str, object], transcript_budget: int) -> Dict[str, object]:
    from ..core import format_local_time, resolve_event_preview

    if not event:
        return {}
    transcript = str(event.get("transcript") or "").strip()
    if transcript_budget > 0 and len(transcript) > transcript_budget:
        transcript = transcript[:transcript_budget].rstrip() + "..."
    return {
        "id": event.get("id"),
        "kind": event.get("kind"),
        "type": event.get("type"),
        "signals": event.get("signals"),
        "severity": event.get("severity"),
        "category": event.get("category"),
        "title": event.get("display_title"),
        "started_at": format_local_time(event.get("started_at")),
        "duration": event.get("duration_label"),
        "priority": event.get("priority_score"),
        "peak": round(float(event.get("peak", 0.0)), 2),
        "speech_ratio": round(float(event.get("speech_ratio", 0.0)), 2),
        "motion_ratio": round(float(event.get("motion_ratio", 0.0)), 3),
        "faces": int(event.get("face_count", 0)),
        "persons": int(event.get("person_count", 0)),
        "language": event.get("transcript_language"),
        "summary": event.get("llm_summary") or event.get("summary"),
        "scene": event.get("scene_description"),
        "transcript": transcript,
        "clip": bool(event.get("video") or event.get("audio")),
        "preview_gif": bool(resolve_event_preview(event)),
    }


def build_priority_items(event_items: List[Dict[str, object]], limit: int) -> List[Dict[str, object]]:
    incidents = build_incidents(event_items)
    return sorted(
        incidents,
        key=lambda item: (SEVERITY_RANK.get(str(item.get("severity") or "low"), 0), int(item.get("priority_score") or 0), str(item.get("started_at") or "")),
        reverse=True,
    )[:limit]


def directory_size_bytes(root: Path) -> int:
    if not root.exists():
        return 0
    total = 0
    for path in root.rglob("*"):
        try:
            if path.is_file():
                total += path.stat().st_size
        except Exception:
            continue
    return total


def directory_file_count(root: Path) -> int:
    if not root.exists():
        return 0
    total = 0
    for path in root.rglob("*"):
        try:
            if path.is_file():
                total += 1
        except Exception:
            continue
    return total


def directory_time_range(root: Path) -> Dict[str, Optional[str]]:
    oldest_mtime = None
    newest_mtime = None
    if root.exists():
        for path in root.rglob("*"):
            try:
                if not path.is_file():
                    continue
                mtime = path.stat().st_mtime
            except Exception:
                continue
            if oldest_mtime is None or mtime < oldest_mtime:
                oldest_mtime = mtime
            if newest_mtime is None or mtime > newest_mtime:
                newest_mtime = mtime
    return {
        "oldest_at": datetime.fromtimestamp(oldest_mtime).astimezone().isoformat(timespec="seconds") if oldest_mtime is not None else None,
        "newest_at": datetime.fromtimestamp(newest_mtime).astimezone().isoformat(timespec="seconds") if newest_mtime is not None else None,
    }


def build_storage_stats() -> Dict[str, object]:
    from ..core import CLIPS_DIR, DATA_DIR, EVENTS_DIR, PREVIEWS_DIR, SNAPSHOTS_DIR, SEGMENTS_DIR

    segments_bytes = directory_size_bytes(SEGMENTS_DIR)
    events_bytes = directory_size_bytes(EVENTS_DIR)
    snapshots_bytes = directory_size_bytes(SNAPSHOTS_DIR)
    clips_bytes = directory_size_bytes(CLIPS_DIR)
    previews_bytes = directory_size_bytes(PREVIEWS_DIR)
    segments_files = directory_file_count(SEGMENTS_DIR)
    events_files = directory_file_count(EVENTS_DIR)
    snapshots_files = directory_file_count(SNAPSHOTS_DIR)
    clips_files = directory_file_count(CLIPS_DIR)
    previews_files = directory_file_count(PREVIEWS_DIR)
    segments_range = directory_time_range(SEGMENTS_DIR)
    events_range = directory_time_range(EVENTS_DIR)
    snapshots_range = directory_time_range(SNAPSHOTS_DIR)
    clips_range = directory_time_range(CLIPS_DIR)
    previews_range = directory_time_range(PREVIEWS_DIR)
    total_bytes = segments_bytes + events_bytes + snapshots_bytes + clips_bytes + previews_bytes
    return {
        "data_dir": str(DATA_DIR),
        "segments_dir": str(SEGMENTS_DIR),
        "events_dir": str(EVENTS_DIR),
        "snapshots_dir": str(SNAPSHOTS_DIR),
        "clips_dir": str(CLIPS_DIR),
        "segments_bytes": segments_bytes,
        "segments_files": segments_files,
        "segments_oldest_at": segments_range["oldest_at"],
        "segments_newest_at": segments_range["newest_at"],
        "events_bytes": events_bytes,
        "events_files": events_files,
        "events_oldest_at": events_range["oldest_at"],
        "events_newest_at": events_range["newest_at"],
        "snapshots_bytes": snapshots_bytes,
        "snapshots_files": snapshots_files,
        "snapshots_oldest_at": snapshots_range["oldest_at"],
        "snapshots_newest_at": snapshots_range["newest_at"],
        "clips_bytes": clips_bytes,
        "clips_files": clips_files,
        "clips_oldest_at": clips_range["oldest_at"],
        "clips_newest_at": clips_range["newest_at"],
        "previews_bytes": previews_bytes,
        "previews_files": previews_files,
        "previews_oldest_at": previews_range["oldest_at"],
        "previews_newest_at": previews_range["newest_at"],
        "total_bytes": total_bytes,
        "total_files": segments_files + events_files + snapshots_files + clips_files + previews_files,
    }


def filter_events_by_hours(event_items: List[Dict[str, object]], hours: int) -> List[Dict[str, object]]:
    if hours <= 0:
        return event_items
    cutoff = time.time() - hours * 3600
    return [
        event for event in event_items
        if float(event.get("started_at_ts") or 0.0) >= cutoff
    ]


def event_matches_filter(event: Dict[str, object], wanted: str) -> bool:
    if not wanted:
        return True
    wanted = wanted.strip().lower()
    event = normalize_event(event)
    return wanted in {
        str(event.get("type") or "").lower(),
        str(event.get("severity") or "").lower(),
        str(event.get("category") or "").lower(),
        str(event.get("category") or "").lower().replace(" ", "_"),
    }
