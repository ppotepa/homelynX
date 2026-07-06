#!/usr/bin/env python3
"""Import critical modules to catch bootstrap regressions."""

from __future__ import annotations

import importlib
import importlib.util
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))


def load_from_path(name: str, path: Path) -> None:
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {name} from {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)


def main() -> int:
    modules = [
        "src.main",
        "src.core.command_handler",
        "src.core.telegram_access",
        "src.core.telegram_callbacks",
        "src.llm.pipeline",
        "src.query.compiler",
    ]
    for module_name in modules:
        importlib.import_module(module_name)

    load_from_path("coord_input_app_smoke", ROOT / "services/coord-input/app.py")
    load_from_path("portal_app_smoke", ROOT / "services/portal/app.py")
    load_from_path("surveillance_app_smoke", ROOT / "services/surveillance/app.py")
    print("Smoke imports passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
