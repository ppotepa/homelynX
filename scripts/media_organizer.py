#!/usr/bin/env python3
"""Homelynx media organizer.

Scans completed downloads and creates a dry-run/apply plan that links/copies/moves
items into a Jellyfin-friendly media library structure using deterministic rules.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import sys
import time
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Any

CATEGORIES = {"movies", "shows", "music", "books", "anime", "software", "games", "other"}
VIDEO_EXT = {".mkv", ".mp4", ".avi", ".mov", ".m4v", ".webm"}
AUDIO_EXT = {".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus"}
BOOK_EXT = {".epub", ".pdf", ".mobi", ".azw3", ".cbz", ".cbr"}
SOFTWARE_EXT = {".iso", ".exe", ".msi", ".deb", ".rpm", ".apk", ".dmg", ".pkg", ".appimage"}
GAME_HINTS = {"game", "gog", "steam", "repack", "fitgirl", "elamigos", "plaza", "codex"}
ANIME_HINTS = {"anime", "subsplease", "horriblesubs", "nyaa", "crunchyroll", "bdremux"}
IGNORE_NAMES = {".ds_store", "thumbs.db", "desktop.ini"}
@dataclass
class FileInfo:
    path: str
    size_bytes: int
    ext: str


@dataclass
class PlanItem:
    id: int
    source: str
    target: str
    action: str
    category: str
    confidence: float
    reason: str
    status: str = "planned"


def safe_name(value: str, fallback: str = "Unknown") -> str:
    value = re.sub(r"[\\/:*?\"<>|]+", " ", value)
    value = re.sub(r"\s+", " ", value).strip(" ._-")
    return value or fallback


def shellish_title(name: str) -> str:
    stem = Path(name).stem
    stem = re.sub(r"[._]+", " ", stem)
    stem = re.sub(r"\[[^\]]+\]|\([^)]*(?:rip|x264|x265|1080p|720p|2160p|web|bluray|flac|mp3)[^)]*\)", " ", stem, flags=re.I)
    stem = re.sub(r"\b(1080p|720p|2160p|480p|x264|x265|h264|h265|web[- ]?dl|webrip|bluray|brrip|dvdrip|hdrip|remux|aac|dts|flac|mp3)\b", " ", stem, flags=re.I)
    return safe_name(stem, Path(name).stem)


def load_dotenv(path: Path) -> None:
    if not path.exists():
        return
    for line in path.read_text(encoding="utf-8", errors="ignore").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


def iter_media_items(source: Path) -> list[Path]:
    if not source.exists():
        return []
    items = []
    for child in sorted(source.iterdir(), key=lambda p: p.name.lower()):
        if child.name.lower() in IGNORE_NAMES:
            continue
        if child.is_file() or child.is_dir():
            items.append(child)
    return items


def collect_files(item: Path, max_files: int = 300) -> list[FileInfo]:
    files: list[FileInfo] = []
    paths = [item] if item.is_file() else list(item.rglob("*"))
    for path in paths:
        if len(files) >= max_files:
            break
        if not path.is_file() or path.name.lower() in IGNORE_NAMES:
            continue
        try:
            size = path.stat().st_size
        except OSError:
            continue
        files.append(FileInfo(path=str(path), size_bytes=size, ext=path.suffix.lower()))
    return files


def audio_tag_guess(files: list[FileInfo], item_name: str) -> tuple[str, str]:
    # Dependency-free fallback. Prefer common "Artist - Album" folder naming.
    name = safe_name(item_name)
    if " - " in name:
        artist, album = name.split(" - ", 1)
        return safe_name(artist, "Unknown Artist"), safe_name(album, "Unknown Album")
    parent = safe_name(name, "Unknown Album")
    return "Unknown Artist", parent


def classify_rules(item: Path, files: list[FileInfo]) -> tuple[str, float, str, str]:
    name = item.name
    lower = name.lower()
    exts = {f.ext for f in files}
    video = [f for f in files if f.ext in VIDEO_EXT]
    audio = [f for f in files if f.ext in AUDIO_EXT]
    books = [f for f in files if f.ext in BOOK_EXT]
    software = [f for f in files if f.ext in SOFTWARE_EXT]
    total = max(len(files), 1)

    if audio and len(audio) / total >= 0.45:
        artist, album = audio_tag_guess(files, name)
        return "music", 0.95, f"audio files detected ({len(audio)} tracks)", f"music/{artist}/{album}"

    if books and len(books) / total >= 0.35:
        title = shellish_title(name)
        return "books", 0.90, "book/document files detected", f"books/Unknown Author/{title}"

    if software:
        title = shellish_title(name)
        return "software", 0.88, "software/archive image detected", f"software/{title}"

    if any(hint in lower for hint in ANIME_HINTS) and video:
        title = shellish_title(name)
        return "anime", 0.78, "anime source/name hint with video", f"anime/{title}"

    if any(hint in lower for hint in GAME_HINTS):
        title = shellish_title(name)
        return "games", 0.76, "game name/source hint", f"games/{title}"

    episode_match = re.search(r"\bS(\d{1,2})E(\d{1,3})\b|\b(\d{1,2})x(\d{1,3})\b", name, re.I)
    if video and episode_match:
        season = episode_match.group(1) or episode_match.group(3) or "1"
        show_name = safe_name(re.split(r"\bS\d{1,2}E\d{1,3}\b|\b\d{1,2}x\d{1,3}\b", name, flags=re.I)[0], "Unknown Show")
        return "shows", 0.92, "episode pattern detected", f"shows/{show_name}/Season {int(season):02d}"

    if video:
        largest = max(video, key=lambda f: f.size_bytes)
        title = shellish_title(Path(largest.path).name if item.is_file() else name)
        year = re.search(r"\b(19\d{2}|20\d{2})\b", name)
        if year and year.group(1) not in title:
            title = f"{title} ({year.group(1)})"
        return "movies", 0.82, "video file detected without episode pattern", f"movies/{title}"

    return "other", 0.35, "no strong rule matched", f"other/{safe_name(name)}"


def safe_relative_target(value: str, category: str) -> str:
    parts = [safe_name(part) for part in Path(value).parts if part not in {"", ".", ".."}]
    if not parts or parts[0] not in CATEGORIES:
        parts = [category] + parts
    if parts[0] != category:
        parts[0] = category
    return "/".join(parts)


def unique_target(path: Path) -> Path:
    if not path.exists():
        return path
    if path.is_dir():
        base = path
        for i in range(2, 1000):
            candidate = base.with_name(f"{base.name} ({i})")
            if not candidate.exists():
                return candidate
    else:
        for i in range(2, 1000):
            candidate = path.with_name(f"{path.stem} ({i}){path.suffix}")
            if not candidate.exists():
                return candidate
    raise RuntimeError(f"Could not find unique target for {path}")


def ensure_inside(base: Path, target: Path) -> None:
    base_resolved = base.resolve()
    target_parent = target.parent.resolve() if target.parent.exists() else target.parent.absolute()
    if base_resolved not in [target_parent, *target_parent.parents]:
        raise ValueError(f"Refusing target outside media library: {target}")


def build_plan(args: argparse.Namespace) -> dict[str, Any]:
    source = Path(args.source).expanduser().resolve()
    library = Path(args.library).expanduser().resolve()
    plans_dir = library / "_organizer" / "plans"
    items: list[PlanItem] = []

    for idx, item in enumerate(iter_media_items(source), start=1):
        files = collect_files(item)
        if not files:
            continue
        category, confidence, reason, target_rel = classify_rules(item, files)
        target_rel = safe_relative_target(target_rel, category)
        target = unique_target(library / target_rel)
        ensure_inside(library, target)
        if confidence < args.min_confidence:
            target = unique_target(library / "other" / safe_name(item.name))
            category = "other"
            reason = f"low confidence fallback: {reason}"
        items.append(PlanItem(
            id=idx,
            source=str(item),
            target=str(target),
            action=args.mode,
            category=category,
            confidence=round(confidence, 3),
            reason=reason,
        ))

    plan = {
        "created_at": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
        "source": str(source),
        "library": str(library),
        "mode": args.mode,
        "dry_run": not args.apply,
        "items": [asdict(item) for item in items],
    }
    plans_dir.mkdir(parents=True, exist_ok=True)
    plan_path = plans_dir / f"plan-{time.strftime('%Y%m%d-%H%M%S')}.json"
    plan_path.write_text(json.dumps(plan, indent=2, ensure_ascii=True), encoding="utf-8")
    plan["plan_path"] = str(plan_path)
    return plan


def link_file(src: Path, dst: Path, mode: str) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    if mode == "hardlink":
        try:
            os.link(src, dst)
        except OSError:
            shutil.copy2(src, dst)
    elif mode == "copy":
        shutil.copy2(src, dst)
    elif mode == "move":
        shutil.move(str(src), str(dst))
    else:
        raise ValueError(f"Unsupported mode: {mode}")


def apply_item(item: PlanItem) -> None:
    src = Path(item.source)
    dst = Path(item.target)
    if src.is_dir():
        for file in src.rglob("*"):
            if not file.is_file():
                continue
            rel = file.relative_to(src)
            target_file = unique_target(dst / rel)
            link_file(file, target_file, item.action)
        if item.action == "move":
            try:
                src.rmdir()
            except OSError:
                pass
    else:
        target = dst / src.name if dst.suffix == "" else dst
        target = unique_target(target)
        link_file(src, target, item.action)
    item.status = "applied"


def apply_plan(plan: dict[str, Any]) -> None:
    for raw in plan["items"]:
        item = PlanItem(**{k: raw[k] for k in PlanItem.__dataclass_fields__.keys() if k in raw})
        apply_item(item)
        raw["status"] = item.status


def print_plan(plan: dict[str, Any]) -> None:
    print(f"Plan: {plan['plan_path']}")
    print(f"Source: {plan['source']}")
    print(f"Library: {plan['library']}")
    print(f"Mode: {plan['mode']} dry_run={plan['dry_run']}")
    if not plan["items"]:
        print("No media items found.")
        return
    for item in plan["items"]:
        print(f"\n{item['id']}. {item['category']} confidence={item['confidence']:.2f} action={item['action']}")
        print(f"   from: {item['source']}")
        print(f"   to:   {item['target']}")
        print(f"   why:  {item['reason']}")


def parse_args() -> argparse.Namespace:
    env_arg = None
    if "--env" in sys.argv:
        try:
            env_arg = sys.argv[sys.argv.index("--env") + 1]
        except IndexError:
            env_arg = ".env"
    load_dotenv(Path(env_arg or ".env"))

    parser = argparse.ArgumentParser(description="Organize completed downloads into a Homelynx media library.")
    parser.add_argument("--source", default=os.getenv("MEDIA_ORGANIZER_SOURCE") or os.getenv("COMPLETED_HOST_PATH") or "")
    parser.add_argument("--library", default=os.getenv("MEDIA_LIBRARY_PATH", "/home/ppotepa/mediaserver"))
    parser.add_argument("--mode", choices=["hardlink", "copy", "move"], default=os.getenv("MEDIA_ORGANIZER_MODE", "hardlink"))
    parser.add_argument("--min-confidence", type=float, default=float(os.getenv("MEDIA_ORGANIZER_MIN_CONFIDENCE", "0.70")))
    parser.add_argument("--apply", action="store_true", help="Apply the generated plan. Default is dry-run only.")
    parser.add_argument("--dry-run", dest="apply", action="store_false", help="Generate and print a plan without changing files.")
    parser.add_argument("--env", default=".env", help="Load defaults from dotenv file before parsing environment-backed paths.")
    args = parser.parse_args()
    if not args.source:
        args.source = str(Path(args.library).expanduser() / "downloads" / "completed")
    return args


def main() -> int:
    args = parse_args()
    plan = build_plan(args)
    if args.apply:
        apply_plan(plan)
        Path(plan["plan_path"]).write_text(json.dumps(plan, indent=2, ensure_ascii=True), encoding="utf-8")
    print_plan(plan)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
