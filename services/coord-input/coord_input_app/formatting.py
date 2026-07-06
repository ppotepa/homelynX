from __future__ import annotations

from html import escape
from typing import Dict

from .storage import history
from .time_utils import local_time

def map_url(record: Dict[str, object]) -> str:
    return f"https://maps.google.com/?q={float(record['lat']):.7f},{float(record['lon']):.7f}"
def format_location(record: Dict[str, object], title: str = "Coordinate update") -> str:
    lat = float(record["lat"])
    lon = float(record["lon"])
    accuracy = record.get("accuracy_m")
    battery = record.get("battery_percent")
    charging = record.get("charging")
    speed = record.get("speed_mps")
    provider = record.get("provider") or "unknown"
    battery_text = "n/a" if battery is None else f"{int(battery)}%" + (" charging" if charging else "")
    accuracy_text = "n/a" if accuracy is None else f"{float(accuracy):.0f} m"
    speed_text = "n/a" if speed is None else f"{float(speed):.1f} m/s"
    return (
        f"<b>{escape(title)}</b>\n"
        f"<b>Device</b>: {escape(str(record.get('device_id') or 'android'))}\n"
        f"<b>Lat</b>: <code>{lat:.7f}</code>\n"
        f"<b>Lon</b>: <code>{lon:.7f}</code>\n"
        f"<b>Accuracy</b>: {escape(accuracy_text)} | <b>Speed</b>: {escape(speed_text)}\n"
        f"<b>Battery</b>: {escape(battery_text)} | <b>Provider</b>: {escape(str(provider))}\n"
        f"<b>Recorded</b>: {escape(local_time(record.get('recorded_at')))}\n"
        f"<a href=\"{escape(map_url(record))}\">Open map</a>"
    )
def format_history(limit: int) -> str:
    items = history(limit)
    if not items:
        return "No coordinates recorded yet."
    lines = [f"<b>Recent coordinates</b> ({len(items)})"]
    for index, item in enumerate(items, 1):
        lines.append(
            f"{index}. <code>{float(item['lat']):.5f},{float(item['lon']):.5f}</code> "
            f"{escape(local_time(item.get('recorded_at')))} "
            f"<a href=\"{escape(map_url(item))}\">map</a>"
        )
    return "\n".join(lines)
