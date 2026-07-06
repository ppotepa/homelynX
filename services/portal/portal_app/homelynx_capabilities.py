from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Any


def load_homelynx_capabilities() -> dict[str, Any]:
    path = Path(os.getenv("HOMELYNX_CAPABILITIES_FILE", "/homelynx-data/capabilities.json"))
    if not path.is_file():
        return {
            "success": True,
            "count": 0,
            "groups": {},
            "source": "homelynx-csharp",
            "note": (
                "Capability manifest not exported yet. "
                "Start homelynx-bot with HOMELYNX_CAPABILITIES_FILE set."
            ),
        }

    with path.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)

    if not isinstance(payload, dict):
        return {"success": False, "count": 0, "groups": {}, "source": "homelynx-csharp", "error": "Invalid manifest"}

    payload.setdefault("source", "homelynx-csharp")
    payload.setdefault("groups", {})
    payload.setdefault("count", 0)
    payload["success"] = True
    return payload