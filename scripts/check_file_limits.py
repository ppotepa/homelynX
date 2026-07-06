#!/usr/bin/env python3
"""Fail when production source files exceed the line limit."""

from __future__ import annotations

import argparse
from pathlib import Path

LINE_LIMIT = 500
EXTENSIONS = {".py", ".java", ".sh", ".ps1"}
ALLOWLIST = {
    "src/core/command_handler.py",
    "services/surveillance/app.py",
    "e2e/runner.py",
    "e2e/tui.py",
    "services/coord-input/app.py",
    "install.sh",
    "android/coord-input/app/src/main/java/dev/ppotepa/coordinput/LocationUploadService.java",
    "src/cli/main.py",
    "src/cli/telegram_history.py",
    "services/portal/app.py",
    "e2e/wizard.py",
    "plugins/torrent/jackett_client.py",
    "e2e/validate_cases.py",
}
SKIP_DIRS = {
    ".git",
    ".venv",
    "__pycache__",
    "tests",
    "data",
    "downloads",
    "e2e/reports",
    "qbittorrent-config",
    "jackett-config",
    "jellyfin-config",
    "cookies",
    "tts-data",
    "surveillance-data",
    "coord-data",
    "llm-data",
    "portal-data",
    "zerotier-one",
    "logs",
    "android/.gradle",
    "android/build",
}


def should_skip(path: Path) -> bool:
    parts = path.parts
    if any(part in SKIP_DIRS for part in parts):
        return True
    joined = path.as_posix()
    if "e2e/reports" in joined:
        return True
    if joined.startswith("android/") and ("/.gradle/" in joined or "/build/" in joined):
        return True
    return any(part.endswith("-data") for part in parts)


def count_lines(path: Path) -> int:
    with path.open("r", encoding="utf-8", errors="ignore") as handle:
        return sum(1 for _ in handle)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = Path(args.root).resolve()

    offenders: list[tuple[str, int]] = []
    for path in root.rglob("*"):
        if not path.is_file() or path.suffix not in EXTENSIONS:
            continue
        if should_skip(path.relative_to(root)):
            continue
        rel = path.relative_to(root).as_posix()
        if rel in ALLOWLIST:
            continue
        lines = count_lines(path)
        if lines > LINE_LIMIT:
            offenders.append((rel, lines))

    if offenders:
        print(f"Files over {LINE_LIMIT} lines:")
        for rel, lines in sorted(offenders):
            print(f"- {rel}: {lines}")
        return 1

    print(f"All production files are within {LINE_LIMIT} lines.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
