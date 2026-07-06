from __future__ import annotations

import re
from datetime import datetime, timezone
from typing import Optional

def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")

def parse_recorded_at(value: Optional[str]) -> str:
    if not value:
        return utc_now()
    text = value.strip()
    try:
        if text.endswith("Z"):
            text = text[:-1] + "+00:00"
        parsed = datetime.fromisoformat(text)
        if parsed.tzinfo is None:
            parsed = parsed.replace(tzinfo=timezone.utc)
        return parsed.astimezone(timezone.utc).isoformat(timespec="seconds")
    except Exception:
        return utc_now()

def parse_duration_to_seconds(value: str) -> int:
    text = str(value or "").strip().lower()
    if not text:
        return 24 * 3600
    if text.isdigit():
        return max(60, int(text) * 60)
    try:
        match = re.fullmatch(r"\s*(\d+(?:\.\d+)?)\s*([a-z]+)\s*", text)
        if not match:
            return 24 * 3600
        number_part = match.group(1)
        unit_part = match.group(2)
        amount = float(number_part or "0")
        if unit_part in {"m", "min", "mins", "minute", "minutes"}:
            return max(60, int(amount * 60))
        if unit_part in {"h", "hr", "hrs", "hour", "hours"}:
            return max(60, int(amount * 3600))
        if unit_part in {"d", "day", "days"}:
            return max(60, int(amount * 86400))
        if unit_part in {"w", "week", "weeks"}:
            return max(60, int(amount * 7 * 86400))
        if unit_part in {"mo", "mon", "month", "months"}:
            return max(60, int(amount * 30 * 86400))
        if unit_part in {"y", "yr", "year", "years"}:
            return max(60, int(amount * 365 * 86400))
    except Exception:
        pass
    return 24 * 3600
def parse_duration_label(value: str) -> str:
    seconds = parse_duration_to_seconds(value)
    days = seconds // 86400
    hours = (seconds % 86400) // 3600
    minutes = (seconds % 3600) // 60
    if days >= 1:
        return f"{days}d" if hours == 0 else f"{days}d {hours}h"
    if hours >= 1:
        return f"{hours}h" if minutes == 0 else f"{hours}h {minutes}m"
    return f"{minutes}m"
def canonical_range_label(value: str) -> str:
    text = str(value or "").strip().lower()
    if not text:
        return "24h"
    if text.isdigit():
        return f"{text}m"
    return text
def format_duration_text(seconds: float) -> str:
    total = max(0, int(round(seconds)))
    if total < 60:
        return f"{total}s"
    minutes, rem = divmod(total, 60)
    if minutes < 60:
        return f"{minutes}m {rem}s" if rem else f"{minutes}m"
    hours, minutes = divmod(minutes, 60)
    if hours < 24:
        return f"{hours}h {minutes}m" if minutes else f"{hours}h"
    days, hours = divmod(hours, 24)
    return f"{days}d {hours}h" if hours else f"{days}d"
def _coord_parse_dt(value: object) -> Optional[datetime]:
    text = str(value or '').strip()
    if not text:
        return None
    try:
        parsed = datetime.fromisoformat(text.replace('Z', '+00:00'))
        if parsed.tzinfo is None:
            parsed = parsed.replace(tzinfo=timezone.utc)
        return parsed.astimezone(timezone.utc)
    except Exception:
        return None
def _coord_local_date(value: object) -> str:
    parsed = _coord_parse_dt(value)
    if not parsed:
        return ''
    return parsed.astimezone().strftime('%Y-%m-%d')
def _coord_seconds_between(start: object, end: object) -> float:
    start_dt = _coord_parse_dt(start)
    end_dt = _coord_parse_dt(end)
    if not start_dt or not end_dt:
        return 0.0
    return max(0.0, (end_dt - start_dt).total_seconds())
def local_time(value: object) -> str:
    text = str(value or "")
    try:
        parsed = datetime.fromisoformat(text.replace("Z", "+00:00"))
        return parsed.astimezone().strftime("%Y-%m-%d %H:%M:%S %Z")
    except Exception:
        return text
