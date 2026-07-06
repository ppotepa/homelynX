from __future__ import annotations

from typing import Optional

from fastapi import HTTPException, Request

from .config import API_KEY

def require_api_key(request: Request, x_coord_key: Optional[str]) -> None:
    if not API_KEY or API_KEY.startswith("change_me"):
        raise HTTPException(status_code=503, detail="COORD_INPUT_API_KEY is not configured")

    auth = request.headers.get("authorization", "").strip()
    bearer = ""
    if auth.lower().startswith("bearer "):
        bearer = auth[7:].strip()

    provided = (x_coord_key or bearer or "").strip()
    if provided != API_KEY:
        raise HTTPException(status_code=401, detail="Invalid coordinate API key")
