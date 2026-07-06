from __future__ import annotations

import sys
from pathlib import Path

SERVICE_DIR = Path(__file__).resolve().parent
if str(SERVICE_DIR) not in sys.path:
    sys.path.insert(0, str(SERVICE_DIR))

from coord_input_app.api import *  # noqa: F403
import coord_input_app.api as _api


def send_telegram(text: str, chat_id: str | None = None, return_message_id: bool = False):
    return _api.send_telegram(text, chat_id, return_message_id)


def bootstrap_allowed(message: dict[str, object], chat_id: str) -> bool:
    _api.send_telegram = send_telegram
    _api.coord_telegram_bot.send_telegram = send_telegram
    return _api.bootstrap_allowed(message, chat_id)


def handle_telegram_message(message: dict[str, object]) -> None:
    _api.send_telegram = send_telegram
    _api.coord_telegram_bot.send_telegram = send_telegram
    _api.handle_telegram_message(message)
