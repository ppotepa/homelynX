from __future__ import annotations

from pathlib import Path
from typing import Optional

import requests

from .config import TELEGRAM_BOT_TOKEN
from .telegram_state import configured_chat_id


def set_error(message: Optional[str]) -> None:
    pass

def telegram_url(method: str) -> str:
    return f"https://api.telegram.org/bot{TELEGRAM_BOT_TOKEN}/{method}"
def send_telegram(text: str, chat_id: Optional[str] = None, return_message_id: bool = False):
    if not TELEGRAM_BOT_TOKEN:
        return None if return_message_id else False
    target = str(chat_id or configured_chat_id()).strip()
    if not target:
        return None if return_message_id else False
    try:
        response = requests.post(
            telegram_url("sendMessage"),
            json={
                "chat_id": target,
                "text": text,
                "parse_mode": "HTML",
                "disable_web_page_preview": True,
            },
            timeout=15,
        )
        if response.status_code >= 400:
            set_error(f"Telegram send failed: {response.status_code} {response.text[:200]}")
            return None if return_message_id else False
        set_error(None)
        if return_message_id:
            payload = response.json()
            result = payload.get("result") or {}
            return result.get("message_id")
        return True
    except Exception as exc:
        set_error(f"Telegram send failed: {exc}")
        return None if return_message_id else False
def edit_telegram_message(chat_id: str, message_id: object, text: str) -> bool:
    if not TELEGRAM_BOT_TOKEN or not chat_id or not message_id:
        return False
    try:
        response = requests.post(
            telegram_url("editMessageText"),
            json={
                "chat_id": chat_id,
                "message_id": message_id,
                "text": text,
                "parse_mode": "HTML",
                "disable_web_page_preview": True,
            },
            timeout=15,
        )
        if response.status_code >= 400:
            set_error(f"Telegram edit failed: {response.status_code} {response.text[:200]}")
            return False
        set_error(None)
        return True
    except Exception as exc:
        set_error(f"Telegram edit failed: {exc}")
        return False
def send_telegram_document(path: Path, caption: str, chat_id: Optional[str] = None) -> bool:
    if not TELEGRAM_BOT_TOKEN:
        return False
    target = str(chat_id or configured_chat_id()).strip()
    if not target or not path.exists():
        return False
    try:
        with path.open("rb") as document:
            response = requests.post(
                telegram_url("sendDocument"),
                data={
                    "chat_id": target,
                    "caption": caption,
                    "disable_content_type_detection": True,
                },
                files={"document": document},
                timeout=60,
            )
        if response.status_code >= 400:
            set_error(f"Telegram document send failed: {response.status_code} {response.text[:200]}")
            return False
        return True
    except Exception as exc:
        set_error(f"Telegram document send failed: {exc}")
        return False
