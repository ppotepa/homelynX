#!/usr/bin/env python3
"""Fail when runtime/generated data is present in the source tree."""

from __future__ import annotations

import argparse
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
RUNTIME_PATHS = [
    "data/llm-audit",
    "e2e/reports",
    "surveillance-data",
    "coord-data",
    "portal-data",
    "llm-data",
    "jellyfin-config",
    "zerotier-one",
]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=ROOT)
    args = parser.parse_args()
    root = Path(args.root).resolve()

    offending = []
    for path in RUNTIME_PATHS:
        try:
            tracked = subprocess.check_output(
                ["git", "-C", str(root), "ls-files", "--", path],
                text=True,
                stderr=subprocess.DEVNULL,
            ).splitlines()
        except subprocess.CalledProcessError:
            tracked = []
        if tracked:
            offending.extend(tracked)

    if offending:
        print("Runtime data detected in git-tracked paths:")
        for path in offending:
            print(f"- {path}")
        return 1

    print("No tracked runtime data detected.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
