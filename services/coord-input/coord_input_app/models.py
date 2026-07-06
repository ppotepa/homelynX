from __future__ import annotations

from datetime import datetime, timezone
from typing import Dict, Optional
from uuid import uuid4

from pydantic import BaseModel, Field

from .time_utils import parse_recorded_at, utc_now

class LocationInput(BaseModel):
    device_id: str = Field(default="android")
    lat: float = Field(ge=-90, le=90)
    lon: float = Field(ge=-180, le=180)
    accuracy_m: Optional[float] = None
    altitude_m: Optional[float] = None
    speed_mps: Optional[float] = None
    bearing_deg: Optional[float] = None
    battery_percent: Optional[int] = None
    charging: Optional[bool] = None
    provider: Optional[str] = None
    recorded_at: Optional[str] = None

def location_to_record(payload: LocationInput) -> Dict[str, object]:
    received_at = utc_now()
    recorded_at = parse_recorded_at(payload.recorded_at)
    return {
        "id": f"loc-{datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')}-{uuid4().hex[:6]}",
        "device_id": payload.device_id.strip() or "android",
        "lat": float(payload.lat),
        "lon": float(payload.lon),
        "accuracy_m": payload.accuracy_m,
        "altitude_m": payload.altitude_m,
        "speed_mps": payload.speed_mps,
        "bearing_deg": payload.bearing_deg,
        "battery_percent": payload.battery_percent,
        "charging": payload.charging,
        "provider": payload.provider,
        "recorded_at": recorded_at,
        "received_at": received_at,
    }
