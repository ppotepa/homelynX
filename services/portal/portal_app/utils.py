from __future__ import annotations

import json
import re
from datetime import datetime, timezone
from typing import Any

TOKEN_RE = re.compile(r"\b\d{8,12}:[A-Za-z0-9_-]{30,}\b")
GITHUB_RE = re.compile(r"\bgh[opsu]_[A-Za-z0-9_]{20,}\b")
BEARER_RE = re.compile(r"(?i)(bearer\s+)[A-Za-z0-9._~+/=-]{20,}")
API_KEY_RE = re.compile(r"(?i)(api[_-]?key|token|secret|password)(['\"\s:=]+)([^,\s'\"]{8,})")

def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")
def redact_text(value: Any) -> str:
    text = value if isinstance(value, str) else json.dumps(value, ensure_ascii=False, default=str)
    text = TOKEN_RE.sub("[redacted-telegram-token]", text)
    text = GITHUB_RE.sub("[redacted-github-token]", text)
    text = BEARER_RE.sub(r"\1[redacted]", text)
    text = API_KEY_RE.sub(r"\1\2[redacted]", text)
    return text
def limit_text(value: Any, limit: int = 200_000) -> str:
    text = redact_text(value or "")
    if len(text) > limit:
        return text[:limit] + "\n[truncated]"
    return text
def redact_log_text(text: str) -> str:
    return limit_text(text, 120_000)
