from __future__ import annotations

import json
import time
from typing import Dict, List, Optional

import requests

from .config import LLM_AUDIT_TOKEN, LLM_AUDIT_URL, LLM_BASE_URL, LLM_ENABLED, LLM_MODEL, LLM_SYSTEM_PROMPT, LLM_TIMEOUT_SECONDS
from .movement import movement_segments
from .time_utils import format_duration_text, local_time


def set_error(message: str) -> None:
    pass

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
    if not LLM_AUDIT_URL or not LLM_AUDIT_TOKEN:
        return
    try:
        requests.post(
            LLM_AUDIT_URL,
            json={
                "service": "coord-input",
                "feature": feature,
                "subject_type": subject_type,
                "subject_id": subject_id,
                "model": LLM_MODEL,
                "status": status,
                "duration_ms": round(duration_ms, 2),
                "prompt": prompt,
                "request_json": request_json,
                "raw_response": raw_response,
                "parsed_response": parsed_response,
                "error": error,
                "metadata": metadata or {},
            },
            headers={"Authorization": f"Bearer {LLM_AUDIT_TOKEN}"},
            timeout=2,
        )
    except Exception:
        pass
def timeline_llm_summary(records: List[Dict[str, object]], summary: Dict[str, object], scope_label: str) -> Dict[str, object]:
    if not LLM_ENABLED or not records:
        return {}
    compact_points = []
    step = max(1, len(records) // 80)
    for record in records[::step][:100]:
        compact_points.append({
            "time": local_time(record.get("received_at")),
            "lat": round(float(record["lat"]), 6),
            "lon": round(float(record["lon"]), 6),
            "accuracy_m": record.get("accuracy_m"),
            "battery_percent": record.get("battery_percent"),
            "speed_mps": record.get("speed_mps"),
        })
    payload = {
        "scope": scope_label,
        "stats": {
            "points": summary.get("count"),
            "distance_km": round(float(summary.get("distance_m") or 0.0) / 1000.0, 2),
            "duration": format_duration_text(float(summary.get("duration_seconds") or 0.0)),
            "first_time": local_time((summary.get("first") or {}).get("received_at")),
            "last_time": local_time((summary.get("last") or {}).get("received_at")),
        },
        "movement_segments": movement_segments(records),
        "sample_points": compact_points,
    }
    prompt = (
        "/no_think\n"
        "You summarize a private location timeline for its owner. Use only the provided data. "
        "Do not invent place names or reasons. Return compact JSON with keys: title, summary, "
        "movement_pattern, notable_points, data_quality. notable_points must be an array of short strings.\n\n"
        f"Timeline JSON:\n{json.dumps(payload, ensure_ascii=False)}"
    )
    request_json = {
        "model": LLM_MODEL,
        "system": LLM_SYSTEM_PROMPT,
        "prompt": prompt,
        "stream": False,
        "format": "json",
        "options": {"temperature": 0, "num_predict": 500},
    }
    started = time.monotonic()
    raw = ""
    try:
        response = requests.post(
            f"{LLM_BASE_URL}/api/generate",
            json=request_json,
            timeout=LLM_TIMEOUT_SECONDS,
        )
        response.raise_for_status()
        response_json = response.json()
        raw = str(response_json.get("response") or "{}").strip()
        parsed = json.loads(raw)
        if isinstance(parsed, dict):
            audit_llm_call(
                "timeline_summary",
                "coord_timeline",
                scope_label,
                prompt,
                request_json,
                raw,
                parsed,
                "success",
                (time.monotonic() - started) * 1000,
                metadata={"points": len(records), "distance_m": summary.get("distance_m")},
            )
            return parsed
    except Exception as exc:
        set_error(f"Coord LLM summary failed: {exc}")
        audit_llm_call(
            "timeline_summary",
            "coord_timeline",
            scope_label,
            prompt,
            request_json,
            raw,
            {},
            "error",
            (time.monotonic() - started) * 1000,
            error=str(exc),
            metadata={"points": len(records), "distance_m": summary.get("distance_m")},
        )
    return {}
