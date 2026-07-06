from __future__ import annotations

import json
import sqlite3
from typing import Dict, List, Optional

from .config import DATA_DIR, DB_PATH, EXPORTS_DIR, HISTORY_LIMIT, STATE_PATH

def ensure_storage() -> None:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    EXPORTS_DIR.mkdir(parents=True, exist_ok=True)
    with sqlite3.connect(DB_PATH) as connection:
        connection.execute(
            """
            CREATE TABLE IF NOT EXISTS location_events (
                id TEXT PRIMARY KEY,
                device_id TEXT NOT NULL,
                lat REAL NOT NULL,
                lon REAL NOT NULL,
                accuracy_m REAL,
                altitude_m REAL,
                speed_mps REAL,
                bearing_deg REAL,
                battery_percent INTEGER,
                charging INTEGER,
                provider TEXT,
                recorded_at TEXT NOT NULL,
                received_at TEXT NOT NULL,
                raw_json TEXT NOT NULL
            )
            """
        )
        connection.execute("CREATE INDEX IF NOT EXISTS idx_location_received ON location_events(received_at)")
        connection.commit()
def store_location(record: Dict[str, object], raw: Dict[str, object]) -> None:
    with sqlite3.connect(DB_PATH) as connection:
        connection.execute(
            """
            INSERT INTO location_events (
                id, device_id, lat, lon, accuracy_m, altitude_m, speed_mps,
                bearing_deg, battery_percent, charging, provider, recorded_at,
                received_at, raw_json
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                record["id"], record["device_id"], record["lat"], record["lon"],
                record.get("accuracy_m"), record.get("altitude_m"), record.get("speed_mps"),
                record.get("bearing_deg"), record.get("battery_percent"),
                None if record.get("charging") is None else int(bool(record.get("charging"))),
                record.get("provider"), record["recorded_at"], record["received_at"],
                json.dumps(raw, ensure_ascii=False, separators=(",", ":")),
            ),
        )
        connection.execute(
            "DELETE FROM location_events WHERE id NOT IN (SELECT id FROM location_events ORDER BY received_at DESC LIMIT ?)",
            (HISTORY_LIMIT,),
        )
        connection.commit()
def row_to_dict(row: sqlite3.Row) -> Dict[str, object]:
    result = dict(row)
    if result.get("charging") is not None:
        result["charging"] = bool(result["charging"])
    result.pop("raw_json", None)
    return result
def latest_location() -> Optional[Dict[str, object]]:
    with sqlite3.connect(DB_PATH) as connection:
        connection.row_factory = sqlite3.Row
        row = connection.execute(
            "SELECT * FROM location_events ORDER BY received_at DESC LIMIT 1"
        ).fetchone()
    return row_to_dict(row) if row else None
def history(limit: int = 20) -> List[Dict[str, object]]:
    safe_limit = max(1, min(limit, 200))
    with sqlite3.connect(DB_PATH) as connection:
        connection.row_factory = sqlite3.Row
        rows = connection.execute(
            "SELECT * FROM location_events ORDER BY received_at DESC LIMIT ?",
            (safe_limit,),
        ).fetchall()
    return [row_to_dict(row) for row in rows]
def history_since(since_iso: str, limit: int = 1000) -> List[Dict[str, object]]:
    safe_limit = max(1, min(limit, 5000))
    with sqlite3.connect(DB_PATH) as connection:
        connection.row_factory = sqlite3.Row
        rows = connection.execute(
            """
            SELECT * FROM location_events
            WHERE received_at >= ?
            ORDER BY received_at ASC
            LIMIT ?
            """,
            (since_iso, safe_limit),
        ).fetchall()
    return [row_to_dict(row) for row in rows]
def history_all(limit: int = 5000) -> List[Dict[str, object]]:
    safe_limit = max(1, min(limit, 5000))
    with sqlite3.connect(DB_PATH) as connection:
        connection.row_factory = sqlite3.Row
        rows = connection.execute(
            """
            SELECT * FROM location_events
            ORDER BY received_at DESC
            LIMIT ?
            """,
            (safe_limit,),
        ).fetchall()
    return [row_to_dict(row) for row in reversed(rows)]
def load_state() -> Dict[str, object]:
    if not STATE_PATH.exists():
        return {}
    try:
        return json.loads(STATE_PATH.read_text(encoding="utf-8"))
    except Exception:
        return {}
def save_state(state: Dict[str, object]) -> None:
    STATE_PATH.parent.mkdir(parents=True, exist_ok=True)
    STATE_PATH.write_text(json.dumps(state, indent=2, ensure_ascii=False), encoding="utf-8")
