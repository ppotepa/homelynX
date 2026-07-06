from __future__ import annotations

import sqlite3
from datetime import datetime, timedelta, timezone

from .config import DATA_DIR, DB_PATH, RETENTION_DAYS

def connect() -> sqlite3.Connection:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(DB_PATH, timeout=30)
    conn.row_factory = sqlite3.Row
    return conn
def ensure_db() -> None:
    with connect() as conn:
        conn.execute(
            """
            CREATE TABLE IF NOT EXISTS llm_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at TEXT NOT NULL,
                service TEXT NOT NULL,
                feature TEXT NOT NULL,
                subject_type TEXT NOT NULL DEFAULT '',
                subject_id TEXT NOT NULL DEFAULT '',
                model TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'unknown',
                duration_ms REAL,
                prompt TEXT NOT NULL DEFAULT '',
                request_json TEXT NOT NULL DEFAULT '{}',
                raw_response TEXT NOT NULL DEFAULT '',
                parsed_response TEXT NOT NULL DEFAULT '',
                error TEXT NOT NULL DEFAULT '',
                metadata_json TEXT NOT NULL DEFAULT '{}',
                prompt_eval_count INTEGER,
                eval_count INTEGER,
                total_duration_ms REAL
            )
            """
        )
        conn.execute("CREATE INDEX IF NOT EXISTS idx_llm_audit_created ON llm_audit(created_at DESC)")
        conn.execute("CREATE INDEX IF NOT EXISTS idx_llm_audit_service ON llm_audit(service, feature, status)")
        conn.commit()
def purge_old() -> None:
    if RETENTION_DAYS <= 0:
        return
    cutoff = (datetime.now(timezone.utc) - timedelta(days=RETENTION_DAYS)).isoformat(timespec="seconds")
    with connect() as conn:
        conn.execute("DELETE FROM llm_audit WHERE created_at < ?", (cutoff,))
        conn.commit()
