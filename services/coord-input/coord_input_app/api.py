from __future__ import annotations

import threading
from pathlib import Path
from typing import Dict, Optional

from fastapi import FastAPI, Header, HTTPException, Request

from coord_input_app.config import (
    API_KEY,
    BOOTSTRAP_CHAT,
    BOT_POLL_SECONDS,
    DATA_DIR,
    EXPORTS_DIR,
    LLM_AUDIT_TOKEN,
    LLM_AUDIT_URL,
    LLM_BASE_URL,
    LLM_ENABLED,
    LLM_MODEL,
    LLM_SYSTEM_PROMPT,
    LLM_TIMEOUT_SECONDS,
    NOTIFY_ENABLED,
    NOTIFY_MIN_DISTANCE_METERS,
    NOTIFY_MIN_INTERVAL_SECONDS,
    TELEGRAM_ALLOWED_USERS,
    TELEGRAM_BOT_TOKEN,
    TELEGRAM_CHAT_ID,
)
from coord_input_app.models import LocationInput, location_to_record
import coord_input_app.llm_summary as coord_llm_summary
from coord_input_app.llm_summary import audit_llm_call, timeline_llm_summary
from coord_input_app.reports import build_coord_report
from coord_input_app.security import require_api_key
from coord_input_app.timeline_html import build_timeline_export
import coord_input_app.telegram_bot as coord_telegram_bot
import coord_input_app.telegram_client as coord_telegram_client
from coord_input_app.formatting import format_history as _format_history, format_location as _format_location, map_url as _map_url
from coord_input_app.notify import should_notify
from coord_input_app.telegram_client import edit_telegram_message as _edit_telegram_message, send_telegram as _send_telegram, send_telegram_document as _send_telegram_document, telegram_url as _telegram_url
from coord_input_app.telegram_state import configured_chat_id, remember_chat_id
from coord_input_app.storage import (
    ensure_storage,
    history,
    history_all,
    history_since,
    latest_location,
    load_state,
    save_state,
    store_location,
)
from coord_input_app.geo import (
    _coord_avg,
    _coord_center,
    haversine_m,
    summarize_locations,
    timeline_points,
)
from coord_input_app.movement import movement_segments
from coord_input_app.time_utils import (
    parse_recorded_at,
    utc_now,
    _coord_local_date,
    _coord_parse_dt,
    _coord_seconds_between,
    canonical_range_label,
    format_duration_text,
    local_time,
    parse_duration_label,
    parse_duration_to_seconds,
)

app = FastAPI(title="Coord Input", version="1.0")
state_lock = threading.Lock()
last_error: Optional[str] = None

def set_error(message: Optional[str]) -> None:
    global last_error
    with state_lock:
        last_error = message


coord_llm_summary.set_error = set_error

coord_telegram_client.set_error = set_error
coord_telegram_bot.set_error = set_error
coord_telegram_bot.get_last_error = lambda: last_error


def telegram_url(method: str) -> str:
    return _telegram_url(method)


def map_url(record: Dict[str, object]) -> str:
    return _map_url(record)


def format_location(record: Dict[str, object], title: str = "Coordinate update") -> str:
    return _format_location(record, title)


def send_telegram(text: str, chat_id: Optional[str] = None, return_message_id: bool = False):
    return _send_telegram(text, chat_id, return_message_id)


def edit_telegram_message(chat_id: str, message_id: object, text: str) -> bool:
    return _edit_telegram_message(chat_id, message_id, text)


def send_telegram_document(path: Path, caption: str, chat_id: Optional[str] = None) -> bool:
    return _send_telegram_document(path, caption, chat_id)


def allowed_telegram_user(message: Dict[str, object]) -> bool:
    return coord_telegram_bot.allowed_telegram_user(message)


def allowed_telegram_message(message: Dict[str, object], chat_id: str) -> bool:
    return coord_telegram_bot.allowed_telegram_message(message, chat_id)


def bootstrap_allowed(message: Dict[str, object], chat_id: str) -> bool:
    coord_telegram_bot.send_telegram = send_telegram
    return coord_telegram_bot.bootstrap_allowed(message, chat_id)


def command_text(message: Dict[str, object]) -> str:
    return coord_telegram_bot.command_text(message)


def format_history(limit: int) -> str:
    return _format_history(limit)


def handle_telegram_message(message: Dict[str, object]) -> None:
    coord_telegram_bot.send_telegram = send_telegram
    coord_telegram_bot.edit_telegram_message = edit_telegram_message
    coord_telegram_bot.send_telegram_document = send_telegram_document
    coord_telegram_bot.get_last_error = lambda: last_error
    coord_telegram_bot.handle_telegram_message(message)


def telegram_poll_loop() -> None:
    coord_telegram_bot.send_telegram = send_telegram
    coord_telegram_bot.edit_telegram_message = edit_telegram_message
    coord_telegram_bot.send_telegram_document = send_telegram_document
    coord_telegram_bot.set_error = set_error
    coord_telegram_bot.telegram_poll_loop()

@app.on_event("startup")
def startup() -> None:
    ensure_storage()
    threading.Thread(target=telegram_poll_loop, daemon=True).start()


@app.on_event("shutdown")
def shutdown() -> None:
    coord_telegram_bot.stop_event.set()


@app.get("/health")
def health() -> Dict[str, object]:
    item = latest_location()
    return {
        "ok": True,
        "api_key_configured": bool(API_KEY and not API_KEY.startswith("change_me")),
        "telegram_configured": bool(TELEGRAM_BOT_TOKEN),
        "chat_configured": bool(configured_chat_id()),
        "latest_recorded_at": item.get("recorded_at") if item else None,
        "last_error": last_error,
    }


@app.post("/location")
async def ingest_location(payload: LocationInput, request: Request, x_coord_key: Optional[str] = Header(default=None)) -> Dict[str, object]:
    require_api_key(request, x_coord_key)
    record = location_to_record(payload)
    store_location(record, payload.model_dump())
    notified = False
    if NOTIFY_ENABLED and should_notify(record):
        notified = send_telegram(format_location(record, "Coordinate update"))
    return {"ok": True, "id": record["id"], "notified": notified}


@app.get("/latest")
def get_latest(request: Request, x_coord_key: Optional[str] = Header(default=None)) -> Dict[str, object]:
    require_api_key(request, x_coord_key)
    item = latest_location()
    if not item:
        raise HTTPException(status_code=404, detail="No coordinates recorded yet")
    return item


@app.get("/history")
def get_history(request: Request, limit: int = 20, x_coord_key: Optional[str] = Header(default=None)) -> Dict[str, object]:
    require_api_key(request, x_coord_key)
    return {"items": history(limit)}
