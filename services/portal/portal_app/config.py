from __future__ import annotations

import os
from pathlib import Path

TRUTHY = {"1", "true", "yes", "on"}
APP_PATH = Path(__file__).resolve().parents[1] / "app.py"
DATA_DIR = Path(os.getenv("PORTAL_DATA_DIR", "/data"))
DB_PATH = DATA_DIR / "llm_audit.sqlite3"
INDEX_PATH = APP_PATH.with_name("index.html")
AUTH_ENABLED = os.getenv("PORTAL_AUTH_ENABLED", "true").lower() not in {"0", "false", "no", "off"}
PORTAL_USERNAME = os.getenv("PORTAL_USERNAME", "admin")
PORTAL_PASSWORD_HASH = os.getenv("PORTAL_PASSWORD_HASH", "")
PORTAL_SESSION_SECRET = os.getenv("PORTAL_SESSION_SECRET", "")
PORTAL_CSRF_TOKEN = os.getenv("PORTAL_CSRF_TOKEN", PORTAL_SESSION_SECRET)
LLM_AUDIT_TOKEN = os.getenv("LLM_AUDIT_TOKEN", "")
RETENTION_DAYS = int(os.getenv("LLM_AUDIT_RETENTION_DAYS", "30"))
ENV_CONFIG_PATH = Path(os.getenv("PORTAL_ENV_FILE", "/config/.env"))
ENV_WRITE_ENABLED = os.getenv("PORTAL_ENV_WRITE_ENABLED", "false").lower() in TRUTHY
DOCKER_SOCKET = Path(os.getenv("PORTAL_DOCKER_SOCKET", "/var/run/docker.sock"))
DOCKER_ENABLED = os.getenv("PORTAL_DOCKER_ENABLED", "false").lower() in TRUTHY
SERVICE_CONTAINERS = {
    "homelynx-bot": "homelynx-bot",
    "surveillance": "surveillance",
    "coord-input": "coord-input",
    "tts": "tts",
    "llm": "llm",
    "jellyfin": "jellyfin",
    "qbittorrent": "qbittorrent",
    "jackett": "jackett",
    "flaresolverr": "flaresolverr",
    "portal": "portal",
}
MODULE_FLAGS = {
    "llm": "LLM_ENABLED",
    "surveillance_notifications": "SURV_NOTIFY_ENABLED",
    "surveillance_record_video": "SURV_RECORD_VIDEO",
    "surveillance_record_audio": "SURV_RECORD_AUDIO",
    "coord_notifications": "COORD_NOTIFY_ENABLED",
    "tts_playback": "TTS_PLAYBACK_ENABLED",
    "media_organizer_llm": "MEDIA_ORGANIZER_LLM_ENABLED",
}


def module_config() -> dict[str, str]:
    """Return editable module feature flags exposed by the portal."""
    return dict(MODULE_FLAGS)
