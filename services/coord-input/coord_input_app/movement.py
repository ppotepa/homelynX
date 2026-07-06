from __future__ import annotations

from datetime import datetime
from typing import Dict, List

from .geo import haversine_m

def movement_segments(records: List[Dict[str, object]]) -> List[Dict[str, object]]:
    segments: List[Dict[str, object]] = []
    if len(records) < 2:
        return segments
    current = None
    for prev, curr in zip(records, records[1:]):
        distance = haversine_m(float(prev["lat"]), float(prev["lon"]), float(curr["lat"]), float(curr["lon"]))
        try:
            seconds = max(
                1.0,
                (
                    datetime.fromisoformat(str(curr["received_at"]).replace("Z", "+00:00")) -
                    datetime.fromisoformat(str(prev["received_at"]).replace("Z", "+00:00"))
                ).total_seconds(),
            )
        except Exception:
            seconds = 60.0
        moving = distance >= max(20.0, float(curr.get("accuracy_m") or 0.0) * 1.5)
        if moving:
            if not current:
                current = {
                    "start": prev.get("received_at"),
                    "end": curr.get("received_at"),
                    "distance_m": 0.0,
                    "duration_seconds": 0.0,
                    "points": 1,
                }
            current["end"] = curr.get("received_at")
            current["distance_m"] = float(current["distance_m"]) + distance
            current["duration_seconds"] = float(current["duration_seconds"]) + seconds
            current["points"] = int(current["points"]) + 1
        elif current:
            segments.append(current)
            current = None
    if current:
        segments.append(current)
    return segments[:12]
