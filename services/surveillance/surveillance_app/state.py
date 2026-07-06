from __future__ import annotations


def _core():
    from . import core

    return core


def set_error(message: str) -> None:
    core = _core()
    with core.state_lock:
        core.last_error = message


def clear_error() -> None:
    core = _core()
    with core.state_lock:
        core.last_error = None


def ensure_dirs() -> None:
    core = _core()
    for path in [core.SEGMENTS_DIR, core.EVENTS_DIR, core.SNAPSHOTS_DIR, core.CLIPS_DIR, core.PREVIEWS_DIR]:
        path.mkdir(parents=True, exist_ok=True)
    core.ensure_job_store()


def shutdown_handler(signum, frame) -> None:
    core = _core()
    core.stop_event.set()
