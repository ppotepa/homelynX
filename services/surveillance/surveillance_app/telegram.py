from __future__ import annotations

import time
import uuid
from datetime import datetime, timezone
from html import escape
from pathlib import Path
from typing import Dict

import requests

last_notification_at = 0.0


def _core():
    from . import core

    return core


def event_ready_for_notification(event: Dict[str, object]) -> bool:
    core = _core()
    event = core.normalize_event(event)
    if str(event.get("notification_status") or "") == "sent":
        return False
    if core.PREVIEW_GIF_ENABLED and str(event.get("preview_status") or "") not in {"done", "failed", "skipped"}:
        return False
    if str(event.get("transcript_status") or "") in {"queued", "running"}:
        return False
    if str(event.get("llm_status") or "") in {"queued", "running", "waiting_for_transcript"}:
        return False
    return True


def html_text(value: object, limit: int = 0) -> str:
    text = str(value or "").strip()
    if limit and len(text) > limit:
        text = f"{text[:limit].rstrip()}..."
    return escape(text)


def format_local_time(value: object) -> str:
    text = str(value or "").strip()
    try:
        parsed = datetime.fromisoformat(text)
        return parsed.astimezone().strftime("%Y-%m-%d %H:%M:%S %Z")
    except Exception:
        return text or "n/a"


def telegram_commands_block(event: Dict[str, object]) -> str:
    event_id = html_text(event.get("id"))
    return (
        "<b>Open in surveillance bot</b>\n"
        f"<code>/event {event_id}</code>\n"
        f"<code>/event_preview {event_id}</code>\n"
        f"<code>/event_clip {event_id}</code>\n"
        f"<code>/event_snapshot {event_id}</code>\n"
        f"<code>/event_transcript {event_id}</code>"
    )


def format_transcript_quote(transcript: str, limit: int = 800) -> str:
    transcript = transcript.strip()
    if not transcript:
        return ""
    clipped = html_text(transcript, limit)
    suffix = "\n<i>Transcript clipped. Open full transcript from the main bot.</i>" if len(transcript) > limit else ""
    return f"\n\n<b>Transcript</b>\n<blockquote>{clipped}</blockquote>{suffix}"


def format_transcript_language(event: Dict[str, object]) -> str:
    language = str(event.get("transcript_language") or "").strip().lower()
    if not language:
        return ""
    probability = float(event.get("transcript_language_probability") or 0.0)
    if probability > 0:
        return f"\n<b>Language</b>: <code>{html_text(language)}</code> ({probability:.0%})"
    return f"\n<b>Language</b>: <code>{html_text(language)}</code>"


def should_notify_analysis_update(event: Dict[str, object]) -> bool:
    core = _core()
    if not core.NOTIFY_ENABLED or not core.TELEGRAM_BOT_TOKEN or not core.TELEGRAM_NOTIFICATION_CHAT_ID:
        return False
    event = core.normalize_event(event)
    minimum = core.SEVERITY_RANK.get(core.TELEGRAM_MIN_SEVERITY, core.SEVERITY_RANK["medium"])
    return core.SEVERITY_RANK.get(str(event.get("severity") or "low"), 0) >= minimum


def should_send_event_notification(event: Dict[str, object]) -> bool:
    core = _core()
    global last_notification_at
    if not should_notify_analysis_update(event):
        return False
    now = time.time()
    event = core.normalize_event(event)
    severity = str(event.get("severity") or "low")
    severity_rank = core.SEVERITY_RANK.get(severity, 0)
    event_age = now - float(event.get("started_at_ts") or now)
    if severity_rank < core.SEVERITY_RANK["critical"]:
        if event_age > core.NOTIFY_BACKLOG_SUPPRESS_SECONDS:
            return False
        if now - last_notification_at < core.NOTIFY_MIN_INTERVAL_SECONDS:
            return False
    last_notification_at = now
    return True


def should_build_preview_gif(event: Dict[str, object]) -> bool:
    core = _core()
    return core.PREVIEW_GIF_ENABLED


def format_telegram_alert(event: Dict[str, object]) -> str:
    core = _core()
    severity = html_text(str(event.get("severity") or "low").upper())
    title = html_text(event.get("display_title") or event.get("type") or "Surveillance event")
    category = html_text(event.get("category") or "activity")
    duration = html_text(event.get("duration_label") or f"{event.get('duration_seconds', 0)}s")
    notification = html_text(event.get("llm_notification") or event.get("llm_summary") or event.get("summary") or "Activity detected.", 900)
    transcript = str(event.get("transcript") or "").strip()

    lines = [
        f"<b>{severity} | {title}</b>",
        f"{category} | {duration} | priority {int(event.get('priority_score') or 0)}/100",
        "",
        f"<b>Time</b>: {html_text(format_local_time(event.get('started_at')))}",
        (
            "<b>Signals</b>: "
            f"face={int(event.get('face_count', 0))} "
            f"person={int(event.get('person_count', 0))} "
            f"motion={float(event.get('motion_ratio', 0.0)):.3f} "
            f"speech={float(event.get('speech_ratio', 0.0)):.2f} "
            f"peak={float(event.get('peak', 0.0)):.2f}"
        ),
        "",
        f"<b>Summary</b>\n{notification}",
    ]
    scene = str(event.get("scene_description") or "").strip()
    if scene:
        lines.append(f"\n<b>Scene</b>\n{html_text(scene, 400)}")
    processing = []
    if str(event.get("transcript_status") or "") in {"queued", "running"}:
        processing.append(f"STT={event.get('transcript_status')}")
    if str(event.get("llm_status") or "") in {"queued", "running", "waiting_for_transcript"}:
        processing.append(f"LLM={event.get('llm_status')}")
    if processing:
        lines.append(f"\n<b>Processing</b>: {html_text(', '.join(processing))}")
    text = "\n".join(lines)
    text += format_transcript_quote(transcript)
    text += format_transcript_language(event)
    text += f"\n\n{telegram_commands_block(event)}"
    return text[:3900]


def format_telegram_photo_caption(event: Dict[str, object]) -> str:
    severity = html_text(str(event.get("severity") or "low").upper())
    title = html_text(event.get("display_title") or event.get("type") or "Surveillance event")
    category = html_text(event.get("category") or "activity")
    duration = html_text(event.get("duration_label") or f"{event.get('duration_seconds', 0)}s")
    event_id = html_text(event.get("id"))
    return (
        f"<b>{severity} | {title}</b>\n"
        f"{category} | {duration}\n"
        f"<code>{event_id}</code>"
    )


def format_telegram_analysis_update(event: Dict[str, object], title: str) -> str:
    core = _core()
    event = core.normalize_event(event)
    header = html_text(title)
    event_title = html_text(event.get("display_title") or event.get("type") or "Surveillance event")
    summary = html_text(event.get("llm_notification") or event.get("llm_summary") or event.get("summary") or "", 700)
    transcript = str(event.get("transcript") or "").strip()
    lines = [
        f"<b>{header}</b>",
        f"{html_text(str(event.get('severity') or 'low').upper())} | {event_title}",
        f"<code>{html_text(event.get('id'))}</code>",
    ]
    if summary:
        lines.append(f"\n<b>Summary</b>\n{summary}")
    text = "\n".join(lines)
    text += format_transcript_quote(transcript, 1000)
    text += format_transcript_language(event)
    text += f"\n\n{telegram_commands_block(event)}"
    return text[:3900]


def notify_telegram_analysis_update(event: Dict[str, object], title: str) -> None:
    core = _core()
    if not should_notify_analysis_update(event):
        return
    try:
        requests.post(
            f"https://api.telegram.org/bot{core.TELEGRAM_BOT_TOKEN}/sendMessage",
            json={
                "chat_id": core.TELEGRAM_NOTIFICATION_CHAT_ID,
                "text": format_telegram_analysis_update(event, title),
                "parse_mode": "HTML",
                "disable_web_page_preview": True,
            },
            timeout=10,
        )
    except Exception as exc:
        core.set_error(f"Telegram analysis update failed: {exc}")


def send_telegram_text_chunks(chat_id: str, title: str, body: str, chunk_size: int = 3400) -> None:
    core = _core()
    if not core.TELEGRAM_BOT_TOKEN or not chat_id:
        return
    text = body.strip() or "No summary was generated."
    chunks = []
    while text:
        if len(text) <= chunk_size:
            chunks.append(text)
            break
        split_at = text.rfind("\n", 0, chunk_size)
        if split_at < chunk_size // 2:
            split_at = text.rfind(" ", 0, chunk_size)
        if split_at < chunk_size // 2:
            split_at = chunk_size
        chunks.append(text[:split_at].strip())
        text = text[split_at:].strip()

    total = len(chunks)
    for index, chunk in enumerate(chunks, start=1):
        header = title if total == 1 else f"{title}\nPart {index}/{total}"
        try:
            requests.post(
                f"https://api.telegram.org/bot{core.TELEGRAM_BOT_TOKEN}/sendMessage",
                json={
                    "chat_id": chat_id,
                    "text": f"{header}\n\n{chunk}",
                    "disable_web_page_preview": True,
                },
                timeout=20,
            )
        except Exception as exc:
            core.set_error(f"Telegram summary send failed: {exc}")
            return


def notify_telegram_preview(event: Dict[str, object]) -> bool:
    core = _core()
    if not should_send_event_notification(event):
        return False
    event = core.normalize_event(event)
    preview_path = Path(str(event.get("preview_gif") or ""))
    if not preview_path.exists():
        return False
    event_id = html_text(event.get("id"))
    severity = html_text(str(event.get("severity") or "low").upper())
    kind = html_text(str(event.get("kind") or core.normalize_event_kind(event)).replace("_", " "))
    title = html_text(event.get("display_title") or event.get("type") or "Event")
    category = html_text(event.get("category") or "activity")
    duration = html_text(event.get("duration_label") or f"{event.get('duration_seconds', 0)}s")
    summary = html_text(event.get("llm_notification") or event.get("llm_summary") or event.get("summary") or "", 250)
    signals = core.normalize_signals(event)
    signal_text = ", ".join(name for name, enabled in signals.items() if enabled) or "activity"
    caption = (
        f"<b>{severity} | {title}</b>\n"
        f"<b>Kind</b>: <code>{kind}</code> | {category} | {duration} | GIF {core.PREVIEW_GIF_SPEED:g}x\n"
        f"<b>Start</b>: {html_text(format_local_time(event.get('started_at')))}\n"
        f"<b>End</b>: {html_text(format_local_time(event.get('ended_at') or event.get('updated_at')))}\n"
        f"<b>Signals</b>: {html_text(signal_text)}\n"
        f"<b>Metrics</b>: face={int(event.get('face_count', 0))} person={int(event.get('person_count', 0))} "
        f"motion={float(event.get('motion_ratio', 0.0)):.3f} speech={float(event.get('speech_ratio', 0.0)):.2f} peak={float(event.get('peak', 0.0)):.2f}\n"
        f"<b>Summary</b>: {summary or 'No summary available.'}"
        f"{format_transcript_quote(str(event.get('transcript') or '').strip(), 350)}"
        f"{format_transcript_language(event)}\n\n"
        f"<code>/event {event_id}</code>\n"
        f"<code>/event_clip {event_id}</code>\n"
        f"<code>/event_transcript {event_id}</code>"
    )
    try:
        with preview_path.open("rb") as animation:
            response = requests.post(
                f"https://api.telegram.org/bot{core.TELEGRAM_BOT_TOKEN}/sendAnimation",
                data={
                    "chat_id": core.TELEGRAM_NOTIFICATION_CHAT_ID,
                    "caption": caption,
                    "parse_mode": "HTML",
                },
                files={"animation": animation},
                timeout=60,
            )
            if response.status_code >= 400:
                core.set_error(f"Telegram preview GIF failed: {response.status_code} {response.text[:200]}")
                return False
            return True
    except Exception as exc:
        core.set_error(f"Telegram preview GIF failed: {exc}")
        return False


def notify_telegram(event: Dict[str, object]) -> None:
    core = _core()
    if not core.NOTIFY_ENABLED or not core.TELEGRAM_BOT_TOKEN or not core.TELEGRAM_NOTIFICATION_CHAT_ID:
        return

    event = core.normalize_event(event)
    if core.SEVERITY_RANK.get(str(event.get("severity") or "low"), 0) < core.SEVERITY_RANK.get(core.TELEGRAM_MIN_SEVERITY, 50):
        return

    text = format_telegram_alert(event)
    try:
        requests.post(
            f"https://api.telegram.org/bot{core.TELEGRAM_BOT_TOKEN}/sendMessage",
            json={
                "chat_id": core.TELEGRAM_NOTIFICATION_CHAT_ID,
                "text": text,
                "parse_mode": "HTML",
                "disable_web_page_preview": True,
            },
            timeout=10,
        )

        snapshot = core.resolve_event_snapshot(event)
        if snapshot and Path(snapshot).exists():
            with Path(snapshot).open("rb") as image:
                requests.post(
                    f"https://api.telegram.org/bot{core.TELEGRAM_BOT_TOKEN}/sendPhoto",
                    data={
                        "chat_id": core.TELEGRAM_NOTIFICATION_CHAT_ID,
                        "caption": format_telegram_photo_caption(event),
                        "parse_mode": "HTML",
                    },
                    files={"photo": image},
                    timeout=30,
                )
    except Exception as exc:
        core.set_error(f"Telegram notify failed: {exc}")


def device_alert_due(now_ts: float) -> bool:
    core = _core()
    return (now_ts - core.last_device_alert_at) >= core.DEVICE_ALERT_COOLDOWN_SECONDS


def create_system_event(event_type: str, summary: str) -> Dict[str, object]:
    core = _core()
    now = time.time()
    started_at = datetime.fromtimestamp(now, tz=timezone.utc).astimezone().isoformat(timespec="seconds")
    event_id = f"evt-{datetime.fromtimestamp(now, tz=timezone.utc).astimezone().strftime('%Y%m%d-%H%M%S')}-{uuid.uuid4().hex[:6]}"
    event = {
        "id": event_id,
        "state": "finalized",
        "kind": core.TYPE_KIND_MAP.get(event_type, "ACTIVITY"),
        "type": event_type,
        "signals": {
            "movement": False,
            "voice": False,
            "face": False,
            "person": False,
            "loud": False,
        },
        "event_types": [],
        "started_at": started_at,
        "started_at_ts": now,
        "updated_at": started_at,
        "updated_at_ts": now,
        "ended_at": started_at,
        "ended_at_ts": now,
        "duration_seconds": 0,
        "duration_label": "0s",
        "severity": "high" if event_type == "device_error" else "medium",
        "category": "system",
        "summary": summary,
        "display_title": summary,
        "priority_score": 95 if event_type == "device_error" else 80,
        "transcript_status": "skipped",
        "llm_status": "skipped",
        "notification_status": "pending",
        "preview_status": "skipped",
        "snapshot": None,
        "annotated_snapshot": None,
        "preview_gif": None,
    }
    return core.normalize_event(event)
