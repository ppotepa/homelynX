from __future__ import annotations

import time
from typing import Dict


def _core():
    from .. import core

    return core


def segment_watcher_loop() -> None:
    core = _core()
    seen: Dict[str, int] = {}
    stable_since: Dict[str, float] = {}
    processed = {str(path) for path in core.SEGMENTS_DIR.glob("**/*.mp4")}
    while not core.stop_event.is_set():
        now = time.time()
        for path in sorted(core.SEGMENTS_DIR.glob("**/*.mp4")):
            key = str(path)
            if key in processed:
                continue
            try:
                size = path.stat().st_size
            except Exception:
                continue
            if key not in seen or seen[key] != size:
                seen[key] = size
                stable_since[key] = now
                continue
            if now - stable_since.get(key, now) < 1.5:
                continue
            if core.process_recorded_segment(path):
                processed.add(key)
                seen.pop(key, None)
                stable_since.pop(key, None)
        core.stop_event.wait(0.5)
