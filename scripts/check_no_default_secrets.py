#!/usr/bin/env python3
"""Fail when weak or placeholder secrets appear in source files."""

from __future__ import annotations

import argparse
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BANNED_LITERALS = (
    "adminadmin",
    "adminpass",
    "your_telegram_bot_token_here",
)
SKIP_PARTS = {
    ".git",
    ".venv",
    "__pycache__",
    "tests",
    "data",
    "downloads",
    "e2e",
    "e2e-tests",
    ".codegraph",
    "qbittorrent-config",
    "jackett-config",
    "jellyfin-config",
    "cookies",
    "tts-data",
    "llm-data",
    "portal-data",
    "zerotier-one",
    "logs",
}
SKIP_EXACT = {
    ".env.example",
    "concat.txt",
    "concat.zip",
    "scripts/check_no_default_secrets.py",
}


def should_skip(path: Path) -> bool:
    parts = set(path.parts)
    if parts & SKIP_PARTS:
        return True
    return any(part.endswith("-data") for part in path.parts)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=ROOT)
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

    offending: list[tuple[str, str]] = []
    for rel in tracked:
        if not rel:
            continue
        relative_path = Path(rel)
        relative_name = relative_path.as_posix()
        if relative_name in SKIP_EXACT or should_skip(relative_path):
            continue
        path = root / relative_path
        if not path.is_file():
            continue
        try:
            text = path.read_text(encoding="utf-8", errors="ignore")
        except Exception:
            continue
        for literal in BANNED_LITERALS:
            if literal in text:
                offending.append((relative_name, literal))

    if offending:
        print("Default secret values detected:")
        for relative_path, literal in offending:
            print(f"- {relative_path}: {literal}")
        return 1

    print("No default secret values detected.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
