from __future__ import annotations

from typing import Any

from fastapi import Depends, FastAPI
from fastapi.responses import HTMLResponse

from .auth import require_portal_auth
from .config import AUTH_ENABLED, DB_PATH, INDEX_PATH
from .html import LLM_HTML


def register_public_routes(app: FastAPI) -> None:
    @app.get("/health")
    def health() -> dict[str, Any]:
        return {"success": True, "service": "portal", "auth_enabled": AUTH_ENABLED, "llm_audit_db": str(DB_PATH)}

    @app.get("/", response_class=HTMLResponse)
    def index() -> str:
        return INDEX_PATH.read_text(encoding="utf-8")

    @app.get("/llm", response_class=HTMLResponse)
    def llm_panel(_: None = Depends(require_portal_auth)) -> str:
        return LLM_HTML

    @app.get("/activity", response_class=HTMLResponse)
    def activity_panel(_: None = Depends(require_portal_auth)) -> str:
        return LLM_HTML
