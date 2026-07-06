from __future__ import annotations

import signal

from .core import app, uvicorn
from .state import ensure_dirs, shutdown_handler
from .workers import start_workers
from . import api as _api  # noqa: F401  Registers FastAPI routes on app.


def main() -> None:
    signal.signal(signal.SIGTERM, shutdown_handler)
    signal.signal(signal.SIGINT, shutdown_handler)
    ensure_dirs()
    start_workers()
    uvicorn.run(app, host="0.0.0.0", port=5060)
