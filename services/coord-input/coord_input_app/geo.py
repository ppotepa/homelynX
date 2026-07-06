from __future__ import annotations

import math
from datetime import datetime
from typing import Dict, List, Optional

def haversine_m(lat1: float, lon1: float, lat2: float, lon2: float) -> float:
    radius = 6371000.0
    phi1 = math.radians(lat1)
    phi2 = math.radians(lat2)
    d_phi = math.radians(lat2 - lat1)
    d_lambda = math.radians(lon2 - lon1)
    a = math.sin(d_phi / 2) ** 2 + math.cos(phi1) * math.cos(phi2) * math.sin(d_lambda / 2) ** 2
    return radius * 2 * math.atan2(math.sqrt(a), math.sqrt(1 - a))
def summarize_locations(records: List[Dict[str, object]]) -> Dict[str, object]:
    if not records:
        return {
            "count": 0,
            "distance_m": 0.0,
            "first": None,
            "last": None,
            "duration_seconds": 0.0,
            "devices": [],
        }
    distance = 0.0
    for prev, curr in zip(records, records[1:]):
        distance += haversine_m(float(prev["lat"]), float(prev["lon"]), float(curr["lat"]), float(curr["lon"]))
    first = records[0]
    last = records[-1]
    try:
        duration_seconds = (
            datetime.fromisoformat(last["received_at"].replace("Z", "+00:00")) -
            datetime.fromisoformat(first["received_at"].replace("Z", "+00:00"))
        ).total_seconds()
    except Exception:
        duration_seconds = 0.0
    return {
        "count": len(records),
        "distance_m": distance,
        "first": first,
        "last": last,
        "duration_seconds": duration_seconds,
        "devices": sorted({str(item.get("device_id") or "android") for item in records}),
    }
def timeline_points(records: List[Dict[str, object]]) -> List[Dict[str, object]]:
    points = []
    for index, record in enumerate(records):
        points.append(
            {
                "index": index,
                "device_id": record.get("device_id"),
                "lat": float(record["lat"]),
                "lon": float(record["lon"]),
                "accuracy_m": record.get("accuracy_m"),
                "battery_percent": record.get("battery_percent"),
                "charging": record.get("charging"),
                "provider": record.get("provider"),
                "recorded_at": record.get("recorded_at"),
                "received_at": record.get("received_at"),
                "speed_mps": record.get("speed_mps"),
                "bearing_deg": record.get("bearing_deg"),
            }
        )
    return points
def _coord_avg(values: List[object]) -> Optional[float]:
    numbers = []
    for value in values:
        try:
            if value is not None:
                numbers.append(float(value))
        except Exception:
            pass
    if not numbers:
        return None
    return sum(numbers) / len(numbers)
def _coord_center(points: List[Dict[str, object]]) -> Optional[Dict[str, float]]:
    clean = [point for point in points if point.get('lat') is not None and point.get('lon') is not None]
    if not clean:
        return None
    return {
        'lat': sum(float(point['lat']) for point in clean) / len(clean),
        'lon': sum(float(point['lon']) for point in clean) / len(clean),
    }
