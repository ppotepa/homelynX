from __future__ import annotations

import threading
import time
from typing import Dict, Optional

from .config import NOTIFY_ENABLED, NOTIFY_MIN_DISTANCE_METERS, NOTIFY_MIN_INTERVAL_SECONDS
from .geo import haversine_m

notify_lock = threading.Lock()
last_notify_at = 0.0
last_notify_lat: Optional[float] = None
last_notify_lon: Optional[float] = None

def should_notify(record: Dict[str, object]) -> bool:
    global last_notify_at, last_notify_lat, last_notify_lon
    if not NOTIFY_ENABLED:
        return False
    now = time.time()
    with notify_lock:
        if last_notify_lat is None or last_notify_lon is None:
            last_notify_at = now
            last_notify_lat = float(record["lat"])
            last_notify_lon = float(record["lon"])
            return True
        distance = haversine_m(last_notify_lat, last_notify_lon, float(record["lat"]), float(record["lon"]))
        enough_time = now - last_notify_at >= NOTIFY_MIN_INTERVAL_SECONDS
        enough_distance = distance >= NOTIFY_MIN_DISTANCE_METERS
        if enough_time or enough_distance:
            last_notify_at = now
            last_notify_lat = float(record["lat"])
            last_notify_lon = float(record["lon"])
            return True
    return False
