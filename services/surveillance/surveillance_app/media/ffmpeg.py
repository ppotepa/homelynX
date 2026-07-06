from __future__ import annotations

import subprocess
from pathlib import Path
from typing import List, Optional


def _core():
    from .. import core

    return core


def run_command(command: List[str], timeout: Optional[int] = None) -> subprocess.CompletedProcess:
    return subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout, check=False)


def write_concat_list(paths: List[str], list_path: Path) -> None:
    lines = []
    for path in paths:
        escaped = path.replace("'", "'\\''")
        lines.append(f"file '{escaped}'")
    list_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def concat_media(paths: List[str], output_path: Path, timeout: int = 60) -> bool:
    if not paths:
        return False
    list_path = output_path.with_suffix(".txt")
    write_concat_list(paths, list_path)
    command = [
        "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
        "-f", "concat", "-safe", "0", "-i", str(list_path),
        "-c", "copy",
        str(output_path),
    ]
    result = run_command(command, timeout=timeout)
    list_path.unlink(missing_ok=True)
    return result.returncode == 0 and output_path.exists() and output_path.stat().st_size > 1024


def mux_video_audio(video_path: Path, audio_path: Path, output_path: Path, timeout: int = 60) -> bool:
    command = [
        "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
        "-i", str(video_path),
        "-i", str(audio_path),
        "-c:v", "copy",
        "-c:a", "aac",
        "-shortest",
        str(output_path),
    ]
    result = run_command(command, timeout=timeout)
    return result.returncode == 0 and output_path.exists() and output_path.stat().st_size > 1024


def existing_file(value: object) -> Optional[Path]:
    text = str(value or "").strip()
    if not text:
        return None
    path = Path(text)
    return path if path.exists() and path.is_file() else None


def copy_event_file(source: object, target: Path, min_size: int = 1024) -> bool:
    core = _core()
    source_path = existing_file(source)
    if not source_path:
        return False
    try:
        target.parent.mkdir(parents=True, exist_ok=True)
        if source_path.resolve() != target.resolve():
            core.shutil.copy2(source_path, target)
        return target.exists() and target.stat().st_size >= min_size
    except Exception:
        return False


def extract_snapshot_from_video(video_path: Path, output_path: Path) -> bool:
    command = [
        "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
        "-i", str(video_path),
        "-frames:v", "1",
        str(output_path),
    ]
    result = run_command(command, timeout=10)
    return result.returncode == 0 and output_path.exists() and output_path.stat().st_size > 1024


def extract_audio_from_media(media_path: Path, output_path: Path) -> bool:
    core = _core()
    command = [
        "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
        "-i", str(media_path),
        "-vn", "-ac", "1", "-ar", "16000",
    ]
    if core.AUDIO_FILTER and core.AUDIO_FILTER.lower() not in {"none", "off", "false"}:
        command.extend(["-af", core.AUDIO_FILTER])
    command.extend(["-acodec", "pcm_s16le", str(output_path)])
    result = run_command(command, timeout=30)
    return result.returncode == 0 and output_path.exists() and output_path.stat().st_size > 1024

