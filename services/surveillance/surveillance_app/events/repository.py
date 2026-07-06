from __future__ import annotations

import json
import re
import shutil
import time
from dataclasses import asdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Optional, Tuple


def _core():
    from .. import core

    return core


def load_event(event_id: str) -> Optional[Dict[str, object]]:
    core = _core()
    for event_file in core.EVENTS_DIR.glob(f"**/{event_id}/event.json"):
        try:
            return core.normalize_event(json.loads(event_file.read_text(encoding="utf-8")))
        except Exception as exc:
            core.set_error(f"Cannot load event {event_id}: {exc}")
            return None

    for event_file in core.EVENTS_DIR.glob("**/event.json"):
        try:
            payload = json.loads(event_file.read_text(encoding="utf-8"))
        except Exception:
            continue
        if str(payload.get("id")) == event_id:
            return core.normalize_event(payload)

    return None


def load_recent_events(limit: int = 20) -> List[Dict[str, object]]:
    core = _core()
    loaded = []
    if core.EVENTS_DIR.exists():
        for event_file in core.EVENTS_DIR.glob("**/event.json"):
            try:
                loaded.append(core.normalize_event(json.loads(event_file.read_text(encoding="utf-8"))))
            except Exception:
                continue

    loaded.sort(key=lambda event: str(event.get("updated_at") or event.get("started_at") or ""), reverse=True)
    return loaded[:limit]


def event_directory(event_id: str, started_at: str) -> Path:
    core = _core()
    date_part = str(started_at or "").split("T", 1)[0] or datetime.now().astimezone().date().isoformat()
    safe_date = re.sub(r"[^0-9-]", "-", date_part)[:10] or "unknown-date"
    safe_id = re.sub(r"[^A-Za-z0-9_.-]", "-", event_id or "event")
    return core.EVENTS_DIR / safe_date / safe_id


def collect_incident_segments(incident: Dict[str, object]) -> List[Dict[str, object]]:
    seen = set()
    merged: List[Dict[str, object]] = []
    for event in incident.get("events", []):
        for segment in event.get("segments", []):
            segment_id = str(segment.get("id") or "")
            if segment_id and segment_id in seen:
                continue
            if segment_id:
                seen.add(segment_id)
            merged.append(segment)
    merged.sort(key=lambda segment: float(segment.get("timestamp") or 0.0))
    return merged


def resolve_event_snapshot(event: Dict[str, object]) -> Optional[str]:
    core = _core()
    representative_annotated = str(event.get("representative_annotated_snapshot") or "").strip()
    if representative_annotated and Path(representative_annotated).exists():
        return representative_annotated
    annotated = str(event.get("annotated_snapshot") or "").strip()
    if annotated and Path(annotated).exists():
        return annotated
    representative_snapshot = str(event.get("representative_snapshot") or "").strip()
    if representative_snapshot and Path(representative_snapshot).exists():
        return representative_snapshot
    snapshot = str(event.get("snapshot") or "").strip()
    if snapshot and Path(snapshot).exists():
        return snapshot
    segment = core.representative_segment(event)
    if segment:
        segment_snapshot = str(segment.get("snapshot_path") or "").strip()
        if segment_snapshot and Path(segment_snapshot).exists():
            return segment_snapshot
    for fallback_segment in event.get("segments", []):
        segment_snapshot = str(fallback_segment.get("snapshot_path") or "").strip()
        if segment_snapshot and Path(segment_snapshot).exists():
            return segment_snapshot
    return None


def resolve_event_preview(event: Dict[str, object]) -> Optional[str]:
    core = _core()
    preview = str(event.get("preview_gif") or "").strip()
    if preview and Path(preview).exists():
        return preview
    event_id = str(event.get("id") or "").strip()
    if event_id:
        candidate = core.PREVIEWS_DIR / event_id / "preview.gif"
        if candidate.exists():
            return str(candidate)
    return None


def latest_snapshot_file() -> Optional[str]:
    core = _core()
    with core.state_lock:
        if core.snapshot_cache:
            latest_cached = str(core.snapshot_cache[-1].get("path") or "").strip()
            if latest_cached and Path(latest_cached).exists():
                return latest_cached
    with core.state_lock:
        for segment in reversed(core.recent_segments):
            snapshot = str(segment.snapshot_path or "").strip()
            if snapshot and Path(snapshot).exists():
                return snapshot
    return None


def capture_live_snapshot() -> Optional[str]:
    return latest_snapshot_file()


def extract_event_snapshot_from_segments(event: Dict[str, object], output_path: Path) -> bool:
    core = _core()
    annotated_target = output_path.with_name("snapshot-detected.jpg")
    if core.copy_event_file(event.get("representative_annotated_snapshot") or event.get("annotated_snapshot"), annotated_target):
        event["annotated_snapshot"] = str(annotated_target)
        event["representative_annotated_snapshot"] = str(annotated_target)

    if core.copy_event_file(event.get("representative_snapshot"), output_path):
        event["snapshot"] = str(output_path)
        return True

    segment = core.representative_segment(event)
    if segment and core.copy_event_file(segment.get("snapshot_path"), output_path):
        event["snapshot"] = str(output_path)
        return True

    representative_video = core.existing_file(event.get("representative_video") or (segment or {}).get("video_path"))
    if representative_video and core.extract_snapshot_from_video(representative_video, output_path):
        event["snapshot"] = str(output_path)
        return True

    for segment in event.get("segments", []):
        video_path = Path(str(segment.get("video_path") or ""))
        if video_path.exists() and core.extract_snapshot_from_video(video_path, output_path):
            event["snapshot"] = str(output_path)
            return True
    return False


def take_snapshot(output_path: Path) -> bool:
    latest = latest_snapshot_from_memory(output_path)
    if latest and Path(latest).exists() and str(latest) == str(output_path):
        return True
    latest = latest_snapshot_file()
    if latest and Path(latest).exists():
        try:
            output_path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(latest, output_path)
            return output_path.exists() and output_path.stat().st_size > 1024
        except Exception:
            return False
    return False


def capture_camera_snapshot(output_path: Path) -> bool:
    return False


def event_start_tuple() -> tuple[str, float, str]:
    now = time.time()
    dt = datetime.fromtimestamp(now, tz=timezone.utc).astimezone()
    return dt.strftime("%Y%m%d-%H%M%S"), now, dt.isoformat(timespec="seconds")


def segment_name() -> tuple[str, float, str]:
    now = time.time()
    dt = datetime.fromtimestamp(now, tz=timezone.utc).astimezone()
    return dt.strftime("%Y%m%d-%H%M%S"), now, dt.isoformat(timespec="seconds")


def segment_timestamp_from_path(path: Path) -> Tuple[str, float, str]:
    match = re.search(r"(\d{8}-\d{6})", path.stem)
    if match:
        dt = datetime.strptime(match.group(1), "%Y%m%d-%H%M%S").astimezone()
        return match.group(1), dt.timestamp(), dt.isoformat(timespec="seconds")
    fallback_id, ts, started_at = segment_name()
    return fallback_id, ts, started_at


def cache_snapshot(segment) -> None:
    core = _core()
    snapshot_path = str(segment.snapshot_path or "")
    if not snapshot_path:
        return
    path = Path(snapshot_path)
    if not path.exists():
        return
    try:
        data = path.read_bytes()
    except Exception:
        return
    item = {
        "id": segment.id,
        "started_at": segment.started_at,
        "timestamp": segment.timestamp,
        "path": snapshot_path,
        "bytes": data,
        "size": len(data),
    }
    with core.state_lock:
        core.snapshot_cache.append(item)
        core.snapshot_cache_bytes += len(data)
        while core.snapshot_cache and core.snapshot_cache_bytes > core.SNAPSHOT_RAM_LIMIT_BYTES:
            removed = core.snapshot_cache.pop(0)
            core.snapshot_cache_bytes -= int(removed.get("size") or 0)


def latest_snapshot_from_memory(output_path: Optional[Path] = None) -> Optional[str]:
    core = _core()
    with core.state_lock:
        item = core.snapshot_cache[-1] if core.snapshot_cache else None
    if not item:
        return latest_snapshot_file()
    if output_path is None:
        return str(item.get("path") or "")
    try:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_bytes(item.get("bytes") or b"")
        return str(output_path)
    except Exception:
        return str(item.get("path") or "")


def get_recent_segments_for_event(start_ts: float, end_ts: float) -> List[Dict[str, object]]:
    """Select buffered segments for an event with pre-roll and post-roll."""
    core = _core()
    window_start = start_ts - core.PRE_ROLL_SEGMENTS * core.SEGMENT_SECONDS
    window_end = end_ts + core.POST_ROLL_SEGMENTS * core.SEGMENT_SECONDS
    with core.state_lock:
        selected = [
            asdict(segment)
            for segment in core.recent_segments
            if window_start <= segment.timestamp <= window_end
        ]
    return selected


def save_event(event: Dict[str, object]) -> None:
    core = _core()
    event = core.normalize_event(event)
    event_dir = event_directory(str(event["id"]), str(event["started_at"]))
    event_dir.mkdir(parents=True, exist_ok=True)
    (event_dir / "event.json").write_text(json.dumps(event, indent=2), encoding="utf-8")
