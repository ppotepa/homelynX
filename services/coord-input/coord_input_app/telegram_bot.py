from __future__ import annotations

import time
import threading
from html import escape
from typing import Dict

import requests

from .config import API_KEY, BOOTSTRAP_CHAT, BOT_POLL_SECONDS, TELEGRAM_ALLOWED_USERS, TELEGRAM_BOT_TOKEN
from .formatting import format_location, format_history
from .storage import latest_location
from .telegram_client import edit_telegram_message, send_telegram, send_telegram_document, telegram_url
from .telegram_state import configured_chat_id, remember_chat_id
from .timeline_html import build_timeline_export
from .time_utils import local_time

stop_event = threading.Event()
last_telegram_update_id = 0


def set_error(message: str | None) -> None:
    pass


def get_last_error() -> str | None:
    return None

def allowed_telegram_user(message: Dict[str, object]) -> bool:
    if not TELEGRAM_ALLOWED_USERS:
        return False
    user = message.get("from") or {}
    return str(user.get("id") or "") in TELEGRAM_ALLOWED_USERS
def allowed_telegram_message(message: Dict[str, object], chat_id: str) -> bool:
    configured = configured_chat_id()
    if TELEGRAM_ALLOWED_USERS:
        return allowed_telegram_user(message)
    if configured:
        return str(chat_id) == configured
    return BOOTSTRAP_CHAT
def bootstrap_allowed(message: Dict[str, object], chat_id: str) -> bool:
    if TELEGRAM_ALLOWED_USERS or not BOOTSTRAP_CHAT:
        return False
    if configured_chat_id():
        return False
    if not chat_id:
        return False
    remember_chat_id(chat_id)
    send_telegram("Coord input chat registered. Commands: /last, /timeline, /history, /status", chat_id)
    return True
def command_text(message: Dict[str, object]) -> str:
    return str(message.get("text") or "").strip()
def handle_telegram_message(message: Dict[str, object]) -> None:
    chat = message.get("chat") or {}
    chat_id = str(chat.get("id") or "").strip()
    if not chat_id:
        return

    bootstrap_allowed(message, chat_id)
    if not allowed_telegram_message(message, chat_id):
        return

    text = command_text(message).split()
    command = text[0].split("@", 1)[0].lower() if text else ""
    if command in {"/start", "/help"}:
        send_telegram(
            "<b>Coord Input commands</b>\n"
            "/last - show latest coordinates\n"
            "/latest - alias for /last\n"
            "/history [n] - show recent coordinates\n"
            "/status - show service status\n"
            "/coord_timeline [17m|30m|1h|35d|1y] - send a Leaflet HTML timeline\n"
            "/timeline [range] - alias for /coord_timeline",
            chat_id,
        )
    elif command in {"/last", "/latest", "/where", "/map"}:
        item = latest_location()
        send_telegram(format_location(item, "Latest coordinates") if item else "No coordinates recorded yet.", chat_id)
    elif command == "/history":
        limit = 10
        if len(text) > 1 and text[1].isdigit():
            limit = int(text[1])
        send_telegram(format_history(limit), chat_id)
    elif command in {"/coord_timeline", "/timeline"}:
        range_value = text[1] if len(text) > 1 else "24h"
        started_at = time.time()
        progress_id = send_telegram(
            f"<b>Generating timeline...</b>\n"
            f"Range: <code>{escape(str(range_value))}</code>\n"
            f"Preparing SQLite export and interactive map.",
            chat_id,
            return_message_id=True,
        )
        export = build_timeline_export(range_value)
        summary = export["summary"]
        elapsed_ms = int((time.time() - started_at) * 1000)
        caption = (
            f"Timeline {export['scope_label']} | "
            f"{summary['count']} points | "
            f"{summary['distance_m'] / 1000.0:.2f} km"
        )
        ready_text = (
            f"<b>Timeline delivered</b>\n"
            f"Range: <code>{escape(str(export['scope_label']))}</code>\n"
            f"Points: {summary['count']}\n"
            f"Distance: {summary['distance_m'] / 1000.0:.2f} km\n"
            f"Generated in: {elapsed_ms} ms\n"
            f"File: <code>{escape(export['path'].name)}</code>\n"
            f"Open the HTML file and use Calendar, ranges, or custom from/to filters."
        )
        document_ok = send_telegram_document(export["path"], caption[:900], chat_id)
        if document_ok:
            if progress_id:
                edit_telegram_message(chat_id, progress_id, ready_text)
            else:
                send_telegram(ready_text, chat_id)
        else:
            failed_text = (
                f"<b>Timeline generated, but delivery failed</b>\n"
                f"Range: <code>{escape(str(export['scope_label']))}</code>\n"
                f"File: <code>{escape(export['path'].name)}</code>\n"
                f"Last error: {escape(get_last_error() or 'unknown')}"
            )
            if progress_id:
                edit_telegram_message(chat_id, progress_id, failed_text)
            else:
                send_telegram(failed_text, chat_id)
    elif command == "/status":
        item = latest_location()
        status = (
            f"<b>Coord Input status</b>\n"
            f"API key configured: {'yes' if bool(API_KEY) else 'no'}\n"
            f"Telegram configured: {'yes' if bool(TELEGRAM_BOT_TOKEN) else 'no'}\n"
            f"Chat configured: {'yes' if bool(configured_chat_id()) else 'no'}\n"
            f"Latest: {escape(local_time(item.get('recorded_at'))) if item else 'none'}\n"
            f"Last error: {escape(get_last_error() or 'none')}"
        )
        send_telegram(status, chat_id)


def telegram_poll_loop() -> None:
    global last_telegram_update_id
    if not TELEGRAM_BOT_TOKEN:
        return
    while not stop_event.is_set():
        try:
            response = requests.get(
                telegram_url("getUpdates"),
                params={"timeout": 20, "offset": last_telegram_update_id + 1},
                timeout=30,
            )
            payload = response.json()
            for update in payload.get("result", []):
                last_telegram_update_id = int(update.get("update_id") or last_telegram_update_id)
                message = update.get("message") or update.get("edited_message")
                if message:
                    handle_telegram_message(message)
            set_error(None)
        except Exception as exc:
            set_error(f"Telegram polling failed: {exc}")
            stop_event.wait(max(2.0, BOT_POLL_SECONDS))
