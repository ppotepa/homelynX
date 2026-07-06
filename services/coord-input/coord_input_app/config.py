from __future__ import annotations

import os
from pathlib import Path

DATA_DIR = Path(os.getenv("COORD_DATA_DIR", "/data"))
DB_PATH = DATA_DIR / "locations.sqlite3"
STATE_PATH = DATA_DIR / "state.json"
EXPORTS_DIR = DATA_DIR / "exports"

API_KEY = os.getenv("COORD_INPUT_API_KEY", "").strip()
TELEGRAM_BOT_TOKEN = os.getenv("COORD_TELEGRAM_BOT_TOKEN", "").strip()
TELEGRAM_CHAT_ID = os.getenv("COORD_TELEGRAM_CHAT_ID", "").strip()
TELEGRAM_ALLOWED_USERS = {
    item.strip()
    for item in os.getenv("COORD_TELEGRAM_ALLOWED_USERS", "").split(",")
    if item.strip()
}
BOOTSTRAP_CHAT = os.getenv("COORD_TELEGRAM_BOOTSTRAP_CHAT", "true").lower() not in {"0", "false", "no", "off"}
NOTIFY_ENABLED = os.getenv("COORD_NOTIFY_ENABLED", "false").lower() not in {"0", "false", "no", "off"}
NOTIFY_MIN_INTERVAL_SECONDS = int(os.getenv("COORD_NOTIFY_MIN_INTERVAL_SECONDS", "60"))
NOTIFY_MIN_DISTANCE_METERS = float(os.getenv("COORD_NOTIFY_MIN_DISTANCE_METERS", "25"))
HISTORY_LIMIT = int(os.getenv("COORD_HISTORY_LIMIT", "5000"))
BOT_POLL_SECONDS = float(os.getenv("COORD_BOT_POLL_SECONDS", "2"))
LLM_ENABLED = os.getenv("COORD_LLM_SUMMARY_ENABLED", os.getenv("LLM_ENABLED", "false")).lower() not in {"0", "false", "no", "off"}
LLM_HOST = os.getenv("LLM_HOST", "llm")
LLM_PORT = int(os.getenv("LLM_PORT", "11434"))
LLM_MODEL = os.getenv("COORD_LLM_MODEL", os.getenv("LLM_MODEL", "qwen3:0.6b"))
LLM_TIMEOUT_SECONDS = int(os.getenv("COORD_LLM_TIMEOUT_SECONDS", "20"))
LLM_BASE_URL = f"http://{LLM_HOST}:{LLM_PORT}"
LLM_AUDIT_URL = os.getenv("LLM_AUDIT_URL", "").strip()
LLM_AUDIT_TOKEN = os.getenv("LLM_AUDIT_TOKEN", "").strip()
LLM_SYSTEM_PROMPT = (
    "You are Homelynx Coord Timeline Analyst, a local privacy-preserving assistant for the owner's location timeline. "
    "Use only provided coordinates, timestamps, movement segments, accuracy, provider, and battery data. "
    "Do not invent place names, reasons, addresses, or activities. "
    "When asked for JSON, return only JSON."
)
