from __future__ import annotations

import hashlib
import hmac
from typing import Optional

from fastapi import Depends, Header, HTTPException
from fastapi.security import HTTPBasic, HTTPBasicCredentials

from .config import AUTH_ENABLED, LLM_AUDIT_TOKEN, PORTAL_CSRF_TOKEN, PORTAL_PASSWORD_HASH, PORTAL_USERNAME

security = HTTPBasic(auto_error=False)

def password_hash_valid(password: str, stored: str) -> bool:
    if not stored:
        return False
    try:
        separator = ":" if ":" in stored else "$"
        scheme, iterations, salt_hex, digest_hex = stored.split(separator, 3)
        if scheme != "pbkdf2_sha256":
            return False
        digest = hashlib.pbkdf2_hmac("sha256", password.encode(), bytes.fromhex(salt_hex), int(iterations)).hex()
        return hmac.compare_digest(digest, digest_hex)
    except Exception:
        return False
def require_portal_auth(credentials: Optional[HTTPBasicCredentials] = Depends(security)) -> None:
    if not AUTH_ENABLED:
        return
    if not credentials:
        raise HTTPException(status_code=401, detail="Authentication required", headers={"WWW-Authenticate": "Basic"})
    if not hmac.compare_digest(credentials.username, PORTAL_USERNAME):
        raise HTTPException(status_code=401, detail="Invalid credentials", headers={"WWW-Authenticate": "Basic"})
    if not password_hash_valid(credentials.password, PORTAL_PASSWORD_HASH):
        raise HTTPException(status_code=401, detail="Invalid credentials", headers={"WWW-Authenticate": "Basic"})
def require_audit_token(authorization: str = Header(default="")) -> None:
    if not LLM_AUDIT_TOKEN:
        raise HTTPException(status_code=503, detail="LLM_AUDIT_TOKEN is not configured")
    expected = f"Bearer {LLM_AUDIT_TOKEN}"
    if not hmac.compare_digest(authorization.strip(), expected):
        raise HTTPException(status_code=401, detail="Invalid audit token")


def require_csrf(x_portal_csrf: str = Header(default="", alias="X-Portal-CSRF")) -> None:
    if not PORTAL_CSRF_TOKEN:
        raise HTTPException(status_code=403, detail="PORTAL_CSRF_TOKEN is not configured")
    if not hmac.compare_digest(x_portal_csrf.strip(), PORTAL_CSRF_TOKEN):
        raise HTTPException(status_code=403, detail="Invalid CSRF token")
