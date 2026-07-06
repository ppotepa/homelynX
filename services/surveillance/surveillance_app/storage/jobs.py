from __future__ import annotations

import json
import os
import sqlite3
import time
import uuid
from typing import Dict, Optional


def _core():
    from .. import core

    return core


def job_connection() -> sqlite3.Connection:
    core = _core()
    connection = sqlite3.connect(str(core.JOBS_DB), timeout=30)
    connection.row_factory = sqlite3.Row
    return connection


def ensure_job_store() -> None:
    core = _core()
    with core.job_lock:
        if core.job_store_ready:
            return
        with job_connection() as connection:
            connection.execute(
                """
                CREATE TABLE IF NOT EXISTS jobs (
                    id TEXT PRIMARY KEY,
                    kind TEXT NOT NULL,
                    event_id TEXT NOT NULL,
                    priority INTEGER NOT NULL DEFAULT 50,
                    status TEXT NOT NULL DEFAULT 'queued',
                    attempts INTEGER NOT NULL DEFAULT 0,
                    max_attempts INTEGER NOT NULL DEFAULT 3,
                    payload_json TEXT NOT NULL DEFAULT '{}',
                    result_json TEXT NOT NULL DEFAULT '{}',
                    error TEXT NOT NULL DEFAULT '',
                    next_run_at REAL NOT NULL,
                    created_at REAL NOT NULL,
                    started_at REAL,
                    finished_at REAL
                )
                """
            )
            connection.execute(
                "CREATE INDEX IF NOT EXISTS idx_jobs_ready ON jobs(status, next_run_at, priority, created_at)"
            )
            connection.execute(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS idx_jobs_active
                ON jobs(kind, event_id)
                WHERE status IN ('queued', 'retry', 'running')
                """
            )
            connection.execute(
                "UPDATE jobs SET status='retry', next_run_at=?, error='Recovered running job after service restart' WHERE status='running'",
                (time.time(),),
            )
            connection.commit()
        core.job_store_ready = True


def job_max_attempts(kind: str) -> int:
    core = _core()
    if kind == "llm_summary":
        return int(os.getenv("SURV_LLM_JOB_MAX_ATTEMPTS", "1"))
    return core.JOB_MAX_ATTEMPTS


def enqueue_job(kind: str, event_id: str, priority: int, payload: Optional[Dict[str, object]] = None, delay_seconds: float = 0.0) -> str:
    ensure_job_store()
    job_id = f"job-{kind}-{event_id}-{uuid.uuid4().hex[:8]}"
    now = time.time()
    try:
        core = _core()
        with core.job_lock:
            with job_connection() as connection:
                connection.execute(
                    """
                    INSERT INTO jobs (
                        id, kind, event_id, priority, status, attempts, max_attempts,
                        payload_json, result_json, error, next_run_at, created_at
                    ) VALUES (?, ?, ?, ?, 'queued', 0, ?, ?, '{}', '', ?, ?)
                    """,
                    (
                        job_id,
                        kind,
                        event_id,
                        int(priority),
                        job_max_attempts(kind),
                        json.dumps(payload or {}, ensure_ascii=False),
                        now + max(0.0, delay_seconds),
                        now,
                    ),
                )
                connection.commit()
        return job_id
    except sqlite3.IntegrityError:
        return ""


def claim_next_job() -> Optional[Dict[str, object]]:
    ensure_job_store()
    now = time.time()
    core = _core()
    with core.job_lock:
        with job_connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            row = connection.execute(
                """
                SELECT * FROM jobs
                WHERE status IN ('queued', 'retry') AND next_run_at <= ?
                ORDER BY priority DESC, next_run_at ASC, created_at ASC
                LIMIT 1
                """,
                (now,),
            ).fetchone()
            if not row:
                connection.commit()
                return None
            connection.execute(
                """
                UPDATE jobs
                SET status='running', attempts=attempts + 1, started_at=?, error=''
                WHERE id=?
                """,
                (now, row["id"]),
            )
            connection.commit()
            job = dict(row)
            job["attempts"] = int(job.get("attempts") or 0) + 1
            return job


def complete_job(job_id: str, result: Optional[Dict[str, object]] = None) -> None:
    core = _core()
    with core.job_lock:
        with job_connection() as connection:
            connection.execute(
                """
                UPDATE jobs
                SET status='done', result_json=?, finished_at=?
                WHERE id=?
                """,
                (json.dumps(result or {}, ensure_ascii=False), time.time(), job_id),
            )
            connection.commit()


def fail_job(job: Dict[str, object], error: str) -> None:
    core = _core()
    attempts = int(job.get("attempts") or 0)
    max_attempts = int(job.get("max_attempts") or core.JOB_MAX_ATTEMPTS)
    status = "failed" if attempts >= max_attempts else "retry"
    delay = min(300, 10 * (2 ** max(0, attempts - 1)))
    with core.job_lock:
        with job_connection() as connection:
            connection.execute(
                """
                UPDATE jobs
                SET status=?, error=?, next_run_at=?, finished_at=?
                WHERE id=?
                """,
                (status, error[:1000], time.time() + delay, time.time(), job["id"]),
            )
            connection.commit()


def job_stats() -> Dict[str, object]:
    ensure_job_store()
    stats = {"queued": 0, "retry": 0, "running": 0, "failed": 0, "done": 0, "oldest_ready_age_seconds": 0}
    now = time.time()
    with job_connection() as connection:
        for row in connection.execute("SELECT status, COUNT(*) AS count FROM jobs GROUP BY status"):
            stats[str(row["status"])] = int(row["count"])
        oldest = connection.execute(
            "SELECT MIN(created_at) AS oldest FROM jobs WHERE status IN ('queued', 'retry')"
        ).fetchone()
        if oldest and oldest["oldest"]:
            stats["oldest_ready_age_seconds"] = max(0, round(now - float(oldest["oldest"])))
    return stats


def cleanup_jobs() -> None:
    core = _core()
    done_cutoff = time.time() - core.JOB_DONE_RETENTION_HOURS * 3600
    failed_cutoff = time.time() - core.JOB_FAILED_RETENTION_HOURS * 3600
    with core.job_lock:
        with job_connection() as connection:
            connection.execute("DELETE FROM jobs WHERE status='done' AND finished_at IS NOT NULL AND finished_at < ?", (done_cutoff,))
            connection.execute("DELETE FROM jobs WHERE status='failed' AND finished_at IS NOT NULL AND finished_at < ?", (failed_cutoff,))
            connection.commit()
