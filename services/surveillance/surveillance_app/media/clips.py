from __future__ import annotations

import math
import uuid
from dataclasses import asdict
from pathlib import Path
from typing import Dict, List

from .ffmpeg import concat_media, mux_video_audio


def _core():
    from .. import core

    return core


def build_recent_clip(seconds: int) -> Dict[str, object]:
    core = _core()
    seconds = max(5, min(seconds, 300))
    needed_segments = max(1, math.ceil(seconds / core.SEGMENT_SECONDS))
    with core.state_lock:
        selected = list(core.recent_segments[-needed_segments:])

    if not selected:
        return {"success": False, "message": "No completed surveillance segments are available yet."}

    return build_clip_from_segments([asdict(segment) for segment in selected], "recent")


def build_clip_from_segments(segments: List[Dict[str, object]], clip_prefix: str) -> Dict[str, object]:
    core = _core()
    if not segments:
        return {"success": False, "message": "No segments available for this clip."}

    clip_id = f"clip-{uuid.uuid4().hex[:10]}"
    clip_dir = core.CLIPS_DIR / clip_id
    clip_dir.mkdir(parents=True, exist_ok=True)
    video_paths = [str(segment.get("video_path")) for segment in segments if segment.get("video_path") and Path(str(segment.get("video_path"))).exists()]
    audio_paths = [str(segment.get("audio_path")) for segment in segments if segment.get("audio_path") and Path(str(segment.get("audio_path"))).exists()]

    video_clip = clip_dir / f"{clip_prefix}-{clip_id}-video.mp4"
    audio_clip = clip_dir / f"{clip_prefix}-{clip_id}-audio.wav"
    output_clip = clip_dir / f"{clip_prefix}-{clip_id}.mp4"

    video_ok = concat_media(video_paths, video_clip) if video_paths else False
    audio_ok = concat_media(audio_paths, audio_clip) if audio_paths else False

    duration = len(segments) * core.SEGMENT_SECONDS
    if video_ok and audio_ok and mux_video_audio(video_clip, audio_clip, output_clip):
        return {
            "success": True,
            "type": "video",
            "file": str(output_clip),
            "segments": len(segments),
            "seconds": duration,
            "message": "video+audio clip",
        }

    if video_ok:
        return {
            "success": True,
            "type": "video",
            "file": str(video_clip),
            "segments": len(segments),
            "seconds": duration,
            "message": "video-only clip",
        }

    if audio_ok:
        return {
            "success": True,
            "type": "audio",
            "file": str(audio_clip),
            "segments": len(segments),
            "seconds": duration,
            "message": "audio-only clip",
        }

    return {"success": False, "message": "No usable audio/video segments found."}


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


def incident_clip(incident: Dict[str, object]) -> Dict[str, object]:
    segments = collect_incident_segments(incident)
    if not segments:
        return {"success": False, "message": "No segments found for this incident."}
    result = build_clip_from_segments(segments, str(incident.get("id") or "incident"))
    if result.get("success"):
        result["event_count"] = int(incident.get("event_count") or 0)
        result["event_types"] = incident.get("event_types") or []
        result["incident_summary"] = incident.get("summary") or ""
    return result


def event_clip(event: Dict[str, object]) -> Dict[str, object]:
    return build_clip_from_segments(event.get("segments", []), str(event.get("id") or "event"))


def recent_clip(seconds: int = 30) -> Dict[str, object]:
    return build_recent_clip(seconds)

