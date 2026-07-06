from __future__ import annotations

import json
import sqlite3
from datetime import datetime, timezone
from typing import Any

from fastapi import Depends, FastAPI, HTTPException, Query, Request

from .auth import require_audit_token, require_csrf, require_portal_auth
from .db import connect, ensure_db, purge_old
from .utils import limit_text, utc_now


def audit_category(item: dict[str, Any] | sqlite3.Row) -> str:
    data = dict(item)
    service = str(data.get("service") or "").lower()
    feature = str(data.get("feature") or "").lower()
    subject_type = str(data.get("subject_type") or "").lower()
    if "surveillance" in service or feature in {"event_summary", "operator_summary"} or subject_type.startswith("surveillance"):
        return "surveillance"
    if "download" in feature or "download" in subject_type or feature in {"media_download", "torrent_download"}:
        return "downloads"
    if "search" in feature or "search" in subject_type:
        return "search"
    if feature in {"natural_intent", "natural_plan", "natural_plan_validation", "natural_step", "natural_response", "natural_button_click"}:
        return "llm"
    if feature == "capability_execute":
        return "capability"
    if "tts" in feature or "tts" in subject_type:
        return "tts"
    if "coord" in service or "coord" in feature:
        return "coords"
    return "system"


def category_clause(category: str) -> tuple[str, list[Any]]:
    category = category.strip().lower()
    if not category:
        return "", []
    if category == "surveillance":
        return "(service LIKE ? OR feature IN ('event_summary','operator_summary') OR subject_type LIKE ?)", ["%surveillance%", "surveillance%"]
    if category == "downloads":
        return "(feature LIKE ? OR subject_type LIKE ? OR feature IN ('media_download','torrent_download'))", ["%download%", "%download%"]
    if category == "search":
        return "(feature LIKE ? OR subject_type LIKE ?)", ["%search%", "%search%"]
    if category == "llm":
        return "feature IN (?, ?, ?, ?, ?, ?)", ["natural_intent", "natural_plan", "natural_plan_validation", "natural_step", "natural_response", "natural_button_click"]
    if category == "capability":
        return "feature = ?", ["capability_execute"]
    if category == "tts":
        return "(feature LIKE ? OR subject_type LIKE ?)", ["%tts%", "%tts%"]
    if category == "coords":
        return "(service LIKE ? OR feature LIKE ?)", ["%coord%", "%coord%"]
    if category == "system":
        return (
            "NOT ("
            "service LIKE ? OR feature IN ('event_summary','operator_summary') OR subject_type LIKE ? OR "
            "feature LIKE ? OR subject_type LIKE ? OR feature IN ('media_download','torrent_download') OR "
            "feature LIKE ? OR subject_type LIKE ? OR "
            "feature IN (?, ?, ?, ?, ?, ?) OR feature = ? OR "
            "feature LIKE ? OR subject_type LIKE ? OR "
            "service LIKE ? OR feature LIKE ?"
            ")"
        ), [
            "%surveillance%", "surveillance%",
            "%download%", "%download%",
            "%search%", "%search%",
            "natural_intent", "natural_plan", "natural_plan_validation", "natural_step", "natural_response", "natural_button_click", "capability_execute",
            "%tts%", "%tts%",
            "%coord%", "%coord%",
        ]
    return "", []


def register_llm_audit_routes(app: FastAPI) -> None:
    @app.post("/api/llm/audit")
    async def create_audit(request: Request, _: None = Depends(require_audit_token)) -> dict[str, Any]:
        ensure_db()
        body = await request.json()
        row = {
            "created_at": str(body.get("created_at") or utc_now()),
            "service": limit_text(body.get("service") or "unknown", 200),
            "feature": limit_text(body.get("feature") or "unknown", 200),
            "subject_type": limit_text(body.get("subject_type") or "", 200),
            "subject_id": limit_text(body.get("subject_id") or "", 500),
            "model": limit_text(body.get("model") or "", 200),
            "status": limit_text(body.get("status") or "unknown", 80),
            "duration_ms": body.get("duration_ms"),
            "prompt": limit_text(body.get("prompt") or ""),
            "request_json": limit_text(body.get("request_json", body.get("request", {}))),
            "raw_response": limit_text(body.get("raw_response") or ""),
            "parsed_response": limit_text(body.get("parsed_response", body.get("parsed", ""))),
            "error": limit_text(body.get("error") or "", 20_000),
            "metadata_json": limit_text(body.get("metadata_json", body.get("metadata", {})), 50_000),
            "prompt_eval_count": body.get("prompt_eval_count"),
            "eval_count": body.get("eval_count"),
            "total_duration_ms": body.get("total_duration_ms"),
        }
        with connect() as conn:
            cur = conn.execute(
                """
                INSERT INTO llm_audit (
                    created_at, service, feature, subject_type, subject_id, model, status,
                    duration_ms, prompt, request_json, raw_response, parsed_response, error,
                    metadata_json, prompt_eval_count, eval_count, total_duration_ms
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                tuple(row.values()),
            )
            conn.commit()
            audit_id = int(cur.lastrowid)
        purge_old()
        return {"success": True, "id": audit_id}

    @app.get("/api/llm/history")
    def llm_history(
        _: None = Depends(require_portal_auth),
        limit: int = Query(default=100, ge=1, le=500),
        category: str = "",
        service: str = "",
        feature: str = "",
        status: str = "",
        q: str = "",
    ) -> dict[str, Any]:
        ensure_db()
        clauses = []
        params: list[Any] = []
        for column, value in (("service", service), ("feature", feature), ("status", status)):
            if value:
                clauses.append(f"{column} = ?")
                params.append(value)
        cat_clause, cat_params = category_clause(category)
        if cat_clause:
            clauses.append(cat_clause)
            params.extend(cat_params)
        if q:
            clauses.append("(subject_id LIKE ? OR prompt LIKE ? OR request_json LIKE ? OR raw_response LIKE ? OR parsed_response LIKE ? OR error LIKE ? OR metadata_json LIKE ?)")
            like = f"%{q}%"
            params.extend([like, like, like, like, like, like, like])
        where = "WHERE " + " AND ".join(clauses) if clauses else ""
        with connect() as conn:
            rows = conn.execute(
                f"""
                SELECT id, created_at, service, feature, subject_type, subject_id, model,
                       status, duration_ms, error, prompt_eval_count, eval_count, total_duration_ms
                FROM llm_audit
                {where}
                ORDER BY created_at DESC, id DESC
                LIMIT ?
                """,
                params + [limit],
            ).fetchall()
        items = []
        for row in rows:
            item = dict(row)
            item["category"] = audit_category(item)
            items.append(item)
        return {"success": True, "items": items}

    @app.get("/api/llm/history/{audit_id}")
    def llm_history_detail(audit_id: int, _: None = Depends(require_portal_auth)) -> dict[str, Any]:
        ensure_db()
        with connect() as conn:
            row = conn.execute("SELECT * FROM llm_audit WHERE id = ?", (audit_id,)).fetchone()
        if not row:
            raise HTTPException(status_code=404, detail="Audit entry not found")
        item = dict(row)
        item["category"] = audit_category(item)
        return {"success": True, "item": item}

    @app.get("/api/llm/stats")
    def llm_stats(_: None = Depends(require_portal_auth)) -> dict[str, Any]:
        ensure_db()
        with connect() as conn:
            status_rows = conn.execute("SELECT status, COUNT(*) count FROM llm_audit GROUP BY status ORDER BY count DESC").fetchall()
            feature_rows = conn.execute("SELECT service, feature, COUNT(*) count FROM llm_audit GROUP BY service, feature ORDER BY count DESC LIMIT 20").fetchall()
            totals = conn.execute("SELECT COUNT(*) count, AVG(duration_ms) avg_ms, MAX(created_at) last_at FROM llm_audit").fetchone()
            all_rows = conn.execute("SELECT service, feature, subject_type FROM llm_audit").fetchall()
        category_counts: dict[str, int] = {}
        for row in all_rows:
            category = audit_category(row)
            category_counts[category] = category_counts.get(category, 0) + 1
        return {
            "success": True,
            "totals": dict(totals or {}),
            "by_status": [dict(row) for row in status_rows],
            "by_feature": [dict(row) for row in feature_rows],
            "by_category": [{"category": key, "count": value} for key, value in sorted(category_counts.items(), key=lambda item: item[1], reverse=True)],
        }

    @app.post("/api/llm/rerun/{audit_id}")
    def llm_rerun(audit_id: int, _: None = Depends(require_portal_auth), __: None = Depends(require_csrf)) -> dict[str, Any]:
        ensure_db()
        with connect() as conn:
            row = conn.execute("SELECT * FROM llm_audit WHERE id = ?", (audit_id,)).fetchone()
        if not row:
            raise HTTPException(status_code=404, detail="Audit entry not found")
        item = dict(row)
        try:
            request_json = json.loads(item.get("request_json") or "{}")
        except Exception:
            raise HTTPException(status_code=400, detail="Stored request_json is not valid JSON")

        import urllib.request as _urlreq

        started = datetime.now(timezone.utc)
        req = _urlreq.Request("http://llm:11434/api/generate", data=json.dumps(request_json).encode(), headers={"Content-Type": "application/json"}, method="POST")
        try:
            with _urlreq.urlopen(req, timeout=60) as resp:
                raw_json = json.loads(resp.read().decode("utf-8", errors="ignore"))
            raw_response = str(raw_json.get("response") or "")
            duration_ms = (datetime.now(timezone.utc) - started).total_seconds() * 1000
            with connect() as conn:
                cur = conn.execute(
                    """
                    INSERT INTO llm_audit (
                        created_at, service, feature, subject_type, subject_id, model, status,
                        duration_ms, prompt, request_json, raw_response, parsed_response, error,
                        metadata_json, prompt_eval_count, eval_count, total_duration_ms
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        utc_now(),
                        item.get("service") or "unknown",
                        f"rerun:{item.get('feature')}",
                        item.get("subject_type") or "rerun",
                        item.get("subject_id") or str(audit_id),
                        request_json.get("model") or item.get("model") or "",
                        "success",
                        round(duration_ms, 2),
                        limit_text(request_json.get("prompt") or item.get("prompt") or ""),
                        limit_text(request_json),
                        limit_text(raw_response),
                        limit_text(raw_response),
                        "",
                        limit_text({"rerun_of": audit_id}),
                        raw_json.get("prompt_eval_count"),
                        raw_json.get("eval_count"),
                        raw_json.get("total_duration"),
                    ),
                )
                conn.commit()
                rerun_id = int(cur.lastrowid)
            return {"success": True, "id": rerun_id, "response": raw_response[:4000], "duration_ms": round(duration_ms, 2)}
        except Exception as exc:
            raise HTTPException(status_code=502, detail=str(exc))
