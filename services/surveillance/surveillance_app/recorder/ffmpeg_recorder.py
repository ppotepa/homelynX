from __future__ import annotations

import subprocess
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict


def _core():
    from .. import core

    return core


def build_recorder_command(day_dir: Path) -> list[str]:
    core = _core()
    output_pattern = str(day_dir / "%Y%m%d-%H%M%S.mp4")
    command = ["ffmpeg", "-hide_banner", "-loglevel", "warning", "-y"]
    if core.RECORD_VIDEO:
        command.extend([
            "-f", "v4l2",
            "-thread_queue_size", "1024",
            "-i", core.CAMERA_DEVICE,
        ])
    if core.RECORD_AUDIO:
        command.extend([
            "-f", "alsa",
            "-thread_queue_size", "1024",
            "-i", core.AUDIO_DEVICE,
        ])
    command.extend([
        "-c:v", "libx264",
        "-preset", "ultrafast",
        "-crf", "28",
        "-c:a", "aac",
        "-b:a", "96k",
        "-f", "segment",
        "-segment_time", str(core.SEGMENT_SECONDS),
        "-reset_timestamps", "1",
        "-strftime", "1",
        output_pattern,
    ])
    return command


def process_recorded_segment(path: Path) -> bool:
    core = _core()
    if not path.exists() or path.stat().st_size < 1024:
        return False
    seg_id, ts, started_at = core.segment_timestamp_from_path(path)
    day = datetime.fromtimestamp(ts).strftime("%Y-%m-%d")
    append_daily_index(day, path)
    audio_path = path.with_suffix(".wav")
    snapshot_path = core.SNAPSHOTS_DIR / day / f"{seg_id}.jpg"
    snapshot_path.parent.mkdir(parents=True, exist_ok=True)
    audio_ok = core.extract_audio_from_media(path, audio_path) if core.RECORD_AUDIO else False
    snapshot_ok = core.extract_snapshot_from_video(path, snapshot_path) if core.RECORD_VIDEO else False
    segment = core.Segment(
        id=seg_id,
        started_at=started_at,
        timestamp=ts,
        audio_path=str(audio_path) if audio_ok else None,
        video_path=str(path) if core.RECORD_VIDEO else None,
        snapshot_path=str(snapshot_path) if snapshot_ok else None,
    )
    core.cache_snapshot(segment)
    with core.state_lock:
        core.last_segment_at = started_at
        core.recent_segments.append(segment)
        del core.recent_segments[:-core.RECENT_SEGMENT_BUFFER]
    core.clear_error()
    core.segment_queue.put(segment)
    return True


def append_daily_index(day: str, segment_path: Path) -> None:
    core = _core()
    day_dir = core.SEGMENTS_DIR / day
    day_dir.mkdir(parents=True, exist_ok=True)
    index_path = day_dir / "day.concat"
    existing = set()
    if index_path.exists():
        existing = set(index_path.read_text(encoding="utf-8").splitlines())
    escaped_path = str(segment_path).replace("'", "'\\''")
    line = f"file '{escaped_path}'"
    if line in existing:
        return
    with index_path.open("a", encoding="utf-8") as handle:
        handle.write(line + "\n")


def recorder_stale() -> bool:
    core = _core()
    if core.last_recorder_restart_at:
        try:
            since_restart = time.time() - datetime.fromisoformat(core.last_recorder_restart_at).timestamp()
            if since_restart < core.RECORDER_WATCHDOG_SECONDS:
                return False
        except Exception:
            pass
    with core.state_lock:
        if not core.last_segment_at:
            return True
        try:
            age = time.time() - datetime.fromisoformat(core.last_segment_at).timestamp()
            return age > core.RECORDER_WATCHDOG_SECONDS
        except Exception:
            return True


def stop_recorder_process() -> None:
    core = _core()
    process = core.recorder_process
    if not process:
        return
    if process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
    core.recorder_process = None


def recorder_loop() -> None:
    core = _core()
    ensure_dirs = core.ensure_dirs
    ensure_dirs()
    while not core.stop_event.is_set():
        ts = time.time()
        day_dir = core.SEGMENTS_DIR / datetime.fromtimestamp(ts).strftime("%Y-%m-%d")
        day_dir.mkdir(parents=True, exist_ok=True)
        command = build_recorder_command(day_dir)
        core.recorder_state = "starting"
        core.last_recorder_restart_at = datetime.now(tz=timezone.utc).astimezone().isoformat(timespec="seconds")
        core.recorder_restarts += 1
        try:
            core.recorder_process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
            core.recorder_state = "recording"
            while not core.stop_event.is_set():
                if datetime.fromtimestamp(time.time()).strftime("%Y-%m-%d") != day_dir.name:
                    core.recorder_state = "rotating_day"
                    stop_recorder_process()
                    break
                if core.recorder_process.poll() is not None:
                    stderr = core.recorder_process.stderr.read().decode("utf-8", errors="replace")[-1000:] if core.recorder_process.stderr else ""
                    core.last_recorder_error = stderr or f"Recorder exited with {core.recorder_process.returncode}"
                    core.set_error(core.last_recorder_error)
                    core.recorder_state = "failed"
                    break
                if recorder_stale():
                    core.last_recorder_error = f"Recorder stale: no complete segment for {core.RECORDER_WATCHDOG_SECONDS}s"
                    core.set_error(core.last_recorder_error)
                    core.recorder_state = "stale"
                    stop_recorder_process()
                    break
                core.stop_event.wait(1)
        except Exception as exc:
            core.last_recorder_error = str(exc)
            core.set_error(f"Recorder failed: {exc}")
            core.recorder_state = "failed"
            stop_recorder_process()
        finally:
            stop_recorder_process()
        if not core.stop_event.is_set():
            core.stop_event.wait(core.RECORDER_RESTART_DELAY_SECONDS)
