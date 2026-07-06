from __future__ import annotations

from pathlib import Path
from typing import Any, Dict, Optional, Tuple

AnalysisResult = Any

SEGMENT_SECONDS = 5
LOUD_PEAK_THRESHOLD = 0.45
NOISE_SPIKE_MULTIPLIER = 2.4
SPEECH_EXTENDED_SEGMENTS = 2

SEVERITY_RANK = {"low": 10, "medium": 50, "high": 75, "critical": 100}
EVENT_KIND_PRIORITY = {
    "PERSON_DETECTION": 500,
    "FACE_DETECTION": 400,
    "VOICE": 300,
    "LOUD": 200,
    "MOVEMENT": 100,
    "ACTIVITY": 10,
    "DEVICE_ERROR": 1000,
    "DEVICE_RECOVERED": 900,
}
KIND_TYPE_MAP = {
    "PERSON_DETECTION": "person_detected",
    "FACE_DETECTION": "face_detected",
    "VOICE": "speech",
    "LOUD": "loud",
    "MOVEMENT": "motion",
    "DEVICE_ERROR": "device_error",
    "DEVICE_RECOVERED": "device_recovered",
    "ACTIVITY": "activity",
}
TYPE_KIND_MAP = {
    "person_detected": "PERSON_DETECTION",
    "face_detected": "FACE_DETECTION",
    "speech": "VOICE",
    "speech_extended": "VOICE",
    "speech+loud": "VOICE",
    "loud": "LOUD",
    "noise_spike": "LOUD",
    "motion": "MOVEMENT",
    "device_error": "DEVICE_ERROR",
    "device_recovered": "DEVICE_RECOVERED",
    "activity": "ACTIVITY",
}
EVENT_TRIGGER_KINDS = {"PERSON_DETECTION", "FACE_DETECTION"}


def analysis_result_score(result: AnalysisResult) -> float:
    score = 0.0
    if result.person_count > 0:
        score += 1200.0 + (result.person_count * 100.0)
    if result.face_count > 0:
        score += 1000.0 + (result.face_count * 100.0)
    if result.noise_spike:
        score += 900.0
    if result.loud:
        score += 700.0
    if result.speech:
        score += 500.0
    if result.motion:
        score += 250.0
    score += min(100.0, result.peak * 100.0)
    score += min(100.0, result.speech_ratio * 100.0)
    score += min(100.0, result.motion_ratio * 1000.0)
    return score


def analysis_result_record(result: AnalysisResult) -> Dict[str, object]:
    return {
        "segment_id": result.segment.id,
        "started_at": result.segment.started_at,
        "timestamp": result.segment.timestamp,
        "audio_path": result.segment.audio_path,
        "video_path": result.segment.video_path,
        "snapshot_path": result.segment.snapshot_path,
        "annotated_snapshot_path": result.annotated_snapshot_path,
        "speech": result.speech,
        "loud": result.loud,
        "noise_spike": result.noise_spike,
        "motion": result.motion,
        "face_count": result.face_count,
        "person_count": result.person_count,
        "motion_ratio": result.motion_ratio,
        "speech_ratio": result.speech_ratio,
        "peak": result.peak,
        "rms": result.rms,
        "score": analysis_result_score(result),
    }


def apply_representative_result(event: Dict[str, object], result: AnalysisResult) -> None:
    record = analysis_result_record(result)
    analysis_segments = event.setdefault("analysis_segments", [])
    if isinstance(analysis_segments, list):
        segment_id = str(record.get("segment_id") or "")
        analysis_segments[:] = [
            item for item in analysis_segments
            if str(item.get("segment_id") or "") != segment_id
        ]
        analysis_segments.append(record)

    current_score = float(event.get("representative_score") or -1.0)
    record_score = float(record.get("score") or 0.0)
    if record_score < current_score:
        return

    event["representative_score"] = record_score
    event["representative_segment_id"] = record.get("segment_id")
    event["representative_at"] = record.get("started_at")
    event["representative_at_ts"] = record.get("timestamp")
    event["representative_video"] = record.get("video_path")
    event["representative_audio"] = record.get("audio_path")
    event["representative_snapshot"] = record.get("snapshot_path")
    if record.get("annotated_snapshot_path"):
        event["representative_annotated_snapshot"] = record.get("annotated_snapshot_path")
        event["annotated_snapshot"] = record.get("annotated_snapshot_path")


def representative_segment(event: Dict[str, object]) -> Optional[Dict[str, object]]:
    segments = event.get("segments") or []
    if not isinstance(segments, list):
        return None

    segment_id = str(event.get("representative_segment_id") or "").strip()
    if segment_id:
        for segment in segments:
            if str(segment.get("id") or "") == segment_id:
                return segment

    target_ts = float(event.get("representative_at_ts") or event.get("started_at_ts") or 0.0)
    if target_ts > 0:
        return min(
            segments,
            key=lambda item: abs(float(item.get("timestamp") or target_ts) - target_ts),
            default=None,
        )
    return segments[0] if segments else None


def representative_media_paths(event: Dict[str, object]) -> Tuple[Optional[Path], Optional[Path]]:
    def existing_file(value: object) -> Optional[Path]:
        text = str(value or "").strip()
        if not text:
            return None
        path = Path(text)
        return path if path.exists() and path.is_file() else None

    segment = representative_segment(event) or {}
    video_path = (
        existing_file(event.get("representative_video"))
        or existing_file(segment.get("video_path"))
        or existing_file(event.get("video"))
    )
    audio_path = (
        existing_file(event.get("representative_audio"))
        or existing_file(segment.get("audio_path"))
        or existing_file(event.get("audio"))
    )
    return video_path, audio_path


def duration_label(seconds: object) -> str:
    try:
        value = max(0, int(float(seconds or 0)))
    except Exception:
        value = 0
    if value < 60:
        return f"{value}s"
    minutes, remainder = divmod(value, 60)
    if minutes < 60:
        return f"{minutes}m {remainder}s" if remainder else f"{minutes}m"
    hours, minutes = divmod(minutes, 60)
    return f"{hours}h {minutes}m" if minutes else f"{hours}h"


def classify_event(event: Dict[str, object]) -> Tuple[str, str, int, str]:
    signals = normalize_signals(event)
    kind = normalize_event_kind(event)
    event_type = KIND_TYPE_MAP.get(kind, str(event.get("type") or "activity"))
    peak = float(event.get("peak", 0.0))
    person_count = int(event.get("person_count", 0))

    if kind == "DEVICE_ERROR" or (signals.get("voice") and signals.get("loud") and peak >= LOUD_PEAK_THRESHOLD):
        severity = "critical"
    elif kind in {"PERSON_DETECTION", "FACE_DETECTION"}:
        severity = "high"
    elif kind in {"VOICE", "LOUD", "MOVEMENT"}:
        severity = "medium"
    else:
        severity = "low"

    if person_count > 0 and peak >= LOUD_PEAK_THRESHOLD:
        severity = "critical"

    if kind == "VOICE":
        category = "voice activity"
    elif kind in {"FACE_DETECTION", "PERSON_DETECTION"}:
        category = "human presence"
    elif kind == "LOUD":
        category = "sound anomaly"
    elif kind == "MOVEMENT":
        category = "movement"
    elif kind in {"DEVICE_ERROR", "DEVICE_RECOVERED"}:
        category = "device health"
    else:
        category = "activity"

    priority = SEVERITY_RANK.get(severity, 10)
    if signals.get("voice") or float(event.get("speech_ratio", 0.0)) > 0:
        priority += 5
    if signals.get("loud") or peak >= LOUD_PEAK_THRESHOLD:
        priority += 10
    if signals.get("person") or person_count > 0:
        priority += 15
    if signals.get("face"):
        priority += 10
    if signals.get("movement"):
        priority += 3

    title_by_kind = {
        "PERSON_DETECTION": "Person detection",
        "FACE_DETECTION": "Face detection",
        "VOICE": "Voice activity",
        "LOUD": "Loud sound",
        "MOVEMENT": "Movement",
        "DEVICE_ERROR": "Recorder problem",
        "DEVICE_RECOVERED": "Recorder recovered",
        "ACTIVITY": "Activity",
    }
    if kind == "VOICE" and signals.get("loud"):
        title = "Voice with loud sound"
    elif kind == "LOUD" and event_type == "noise_spike":
        title = "Noise spike"
    elif kind == "VOICE" and int(event.get("duration_seconds") or 0) >= SPEECH_EXTENDED_SEGMENTS * SEGMENT_SECONDS:
        title = "Extended voice activity"
    else:
        title = title_by_kind.get(kind, "Activity")
    return severity, category, min(priority, 100), title


def normalize_event(event: Dict[str, object]) -> Dict[str, object]:
    signals = normalize_signals(event)
    kind = normalize_event_kind(event)
    event["kind"] = kind
    event["type"] = event_type_for_event(event)
    event["signals"] = signals
    event["event_types"] = sorted([name.upper() for name, enabled in signals.items() if enabled])
    severity, category, priority, title = classify_event(event)
    event["severity"] = severity
    event["category"] = category
    event["priority_score"] = priority
    event["display_title"] = title
    event["duration_label"] = duration_label(event.get("duration_seconds"))
    event["has_transcript"] = bool(str(event.get("transcript") or "").strip())
    event["has_snapshot"] = bool(event.get("snapshot") or event.get("snapshot_path") or event.get("representative_snapshot"))
    event["has_clip"] = bool(event.get("video") or event.get("audio") or event.get("segments"))
    event.setdefault("summary_provider", "rules")
    return event


def normalize_signals(event: Dict[str, object]) -> Dict[str, bool]:
    raw = event.get("signals") if isinstance(event.get("signals"), dict) else {}
    event_type = str(event.get("type") or "").strip().lower()
    return {
        "movement": bool(raw.get("movement") or raw.get("motion") or event_type == "motion" or float(event.get("motion_ratio", 0.0)) >= 0.015),
        "voice": bool(raw.get("voice") or raw.get("speech") or event_type in {"speech", "speech_extended", "speech+loud"} or float(event.get("speech_ratio", 0.0)) >= 0.18),
        "face": bool(raw.get("face") or raw.get("face_detection") or event_type == "face_detected" or int(event.get("face_count", 0)) > 0),
        "person": bool(raw.get("person") or raw.get("person_detection") or event_type == "person_detected" or int(event.get("person_count", 0)) > 0),
        "loud": bool(raw.get("loud") or raw.get("noise_spike") or event_type in {"loud", "noise_spike", "speech+loud"} or float(event.get("peak", 0.0)) >= LOUD_PEAK_THRESHOLD),
    }


def result_signals(result: AnalysisResult) -> Dict[str, bool]:
    return {
        "movement": bool(result.motion),
        "voice": bool(result.speech),
        "face": bool(result.face_count > 0),
        "person": bool(result.person_count > 0),
        "loud": bool(result.loud or result.noise_spike),
    }


def kind_from_signals(signals: Dict[str, bool]) -> str:
    if signals.get("person"):
        return "PERSON_DETECTION"
    if signals.get("face"):
        return "FACE_DETECTION"
    if signals.get("voice"):
        return "VOICE"
    if signals.get("loud"):
        return "LOUD"
    if signals.get("movement"):
        return "MOVEMENT"
    return "ACTIVITY"


def normalize_event_kind(event: Dict[str, object]) -> str:
    raw_kind = str(event.get("kind") or "").strip().upper()
    if raw_kind in EVENT_KIND_PRIORITY:
        return raw_kind
    event_type = str(event.get("type") or "activity").strip().lower()
    if event_type in TYPE_KIND_MAP:
        return TYPE_KIND_MAP[event_type]
    return kind_from_signals(normalize_signals(event))


def event_type_for_event(event: Dict[str, object]) -> str:
    kind = str(event.get("kind") or normalize_event_kind(event)).strip().upper()
    signals = normalize_signals(event)
    if kind == "VOICE":
        if signals.get("loud"):
            return "speech+loud"
        if int(event.get("duration_seconds") or 0) >= SPEECH_EXTENDED_SEGMENTS * SEGMENT_SECONDS:
            return "speech_extended"
        return "speech"
    if kind == "LOUD":
        return "noise_spike" if float(event.get("peak", 0.0)) >= (LOUD_PEAK_THRESHOLD * NOISE_SPIKE_MULTIPLIER) else "loud"
    return KIND_TYPE_MAP.get(kind, str(event.get("type") or "activity"))


def merge_event_signals(event: Dict[str, object], signals: Dict[str, bool]) -> Dict[str, bool]:
    merged = normalize_signals(event)
    for key, value in signals.items():
        merged[key] = bool(merged.get(key) or value)
    event["signals"] = merged
    event["kind"] = max(
        [normalize_event_kind(event), kind_from_signals(merged)],
        key=lambda value: EVENT_KIND_PRIORITY.get(value, 0),
    )
    event["type"] = event_type_for_event(event)
    return merged


def result_has_activity(result: AnalysisResult) -> bool:
    signals = result_signals(result)
    return bool(any(signals.values()) or result.noise_spike)


def result_triggers_event(result: AnalysisResult) -> bool:
    kind = kind_from_signals(result_signals(result))
    return kind in EVENT_TRIGGER_KINDS


def result_has_context(result: AnalysisResult) -> bool:
    signals = result_signals(result)
    return bool(signals.get("movement") or signals.get("voice") or signals.get("loud"))
