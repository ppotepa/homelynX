from __future__ import annotations

from .config import TELEGRAM_CHAT_ID
from .storage import load_state, save_state

def configured_chat_id() -> str:
    if TELEGRAM_CHAT_ID:
        return TELEGRAM_CHAT_ID
    state = load_state()
    return str(state.get("telegram_chat_id") or "").strip()
def remember_chat_id(chat_id: object) -> None:
    state = load_state()
    state["telegram_chat_id"] = str(chat_id)
    save_state(state)
