#!/usr/bin/env python3
"""Detect mojibake markers in tracked source files."""

from __future__ import annotations

import argparse
import subprocess
from pathlib import Path

MARKERS = ("Γ", "≡ƒ", "∩╕", "ΓÇ")
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
    "llm-data",
    "zerotier-one",
    "logs",
}
SKIP_EXACT = {
    "concat.zip",
    "scripts/check_encoding.py",
}


def should_skip(path: Path) -> bool:
    parts = path.parts
    if any(part in SKIP_DIRS for part in parts):
        return True
    joined = path.as_posix()
    if joined in SKIP_EXACT:
        return True
    if joined.startswith("android/") and ("/.gradle/" in joined or "/build/" in joined):
        return True
    if "e2e/reports" in joined:
        return True
    return any(part.endswith("-data") for part in parts)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    root = Path(args.root).resolve()

    try:
        tracked = subprocess.run(
            ["git", "-C", str(root), "ls-files", "-z"],
            check=True,
            capture_output=True,
        ).stdout.decode("utf-8", errors="replace").split("\0")
    except (OSError, subprocess.CalledProcessError):
        tracked = [path.relative_to(root).as_posix() for path in root.rglob("*") if path.is_file()]

    offenders: list[str] = []
    for rel in tracked:
        if not rel:
            continue
        relative = Path(rel)
        if should_skip(relative):
            continue
        path = root / relative
        if not path.is_file():
            continue
        try:
            text = path.read_text(encoding="utf-8", errors="ignore")
        except Exception:
            continue
        if any(marker in text for marker in MARKERS):
            offenders.append(rel)

    if offenders:
        print("Encoding markers detected:")
        for rel in sorted(offenders):
            print(f"- {rel}")
        return 1

    print("No mojibake markers detected.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
