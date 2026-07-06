from __future__ import annotations

from pathlib import Path
from typing import Dict, Optional


def _core():
    from .. import core

    return core


def detect_face_crop_filter(video_path: Path) -> str:
    core = _core()
    classifier = core.get_face_cascade()
    if classifier is None or not video_path.exists():
        return ""

    capture = core.cv2.VideoCapture(str(video_path))
    if not capture.isOpened():
        return ""

    try:
        frame_count = int(capture.get(core.cv2.CAP_PROP_FRAME_COUNT) or 0)
        width = int(capture.get(core.cv2.CAP_PROP_FRAME_WIDTH) or 0)
        height = int(capture.get(core.cv2.CAP_PROP_FRAME_HEIGHT) or 0)
        if frame_count <= 0 or width <= 0 or height <= 0:
            return ""

        boxes = []
        sample_count = min(8, max(2, frame_count))
        for index in range(sample_count):
            frame_index = int((frame_count - 1) * index / max(1, sample_count - 1))
            capture.set(core.cv2.CAP_PROP_POS_FRAMES, frame_index)
            ok, frame = capture.read()
            if not ok or frame is None:
                continue
            gray = core.cv2.cvtColor(frame, core.cv2.COLOR_BGR2GRAY)
            faces = classifier.detectMultiScale(
                gray,
                scaleFactor=1.1,
                minNeighbors=5,
                minSize=(core.FACE_MIN_SIZE, core.FACE_MIN_SIZE),
            )
            for (x, y, w, h) in faces:
                boxes.append((int(x), int(y), int(x + w), int(y + h)))

        if not boxes:
            return ""

        x1 = max(0, min(box[0] for box in boxes))
        y1 = max(0, min(box[1] for box in boxes))
        x2 = min(width, max(box[2] for box in boxes))
        y2 = min(height, max(box[3] for box in boxes))
        box_w = max(1, x2 - x1)
        box_h = max(1, y2 - y1)
        center_x = x1 + box_w / 2
        center_y = y1 + box_h / 2
        crop_w = min(width, max(box_w * 3.0, width * 0.45))
        crop_h = min(height, max(box_h * 3.0, height * 0.45))
        source_aspect = width / max(1, height)
        if crop_w / max(1, crop_h) < source_aspect:
            crop_w = min(width, crop_h * source_aspect)
        else:
            crop_h = min(height, crop_w / source_aspect)
        crop_w = int(max(2, crop_w)) // 2 * 2
        crop_h = int(max(2, crop_h)) // 2 * 2
        crop_x = int(min(max(0, center_x - crop_w / 2), max(0, width - crop_w)))
        crop_y = int(min(max(0, center_y - crop_h / 2), max(0, height - crop_h)))
        return f"crop={crop_w}:{crop_h}:{crop_x}:{crop_y}"
    finally:
        capture.release()


def build_status_preview_gif(event: Dict[str, object], output_path: Path, width: int) -> Dict[str, object]:
    core = _core()
    event_id = str(event.get("id") or "event")
    timestamp_text = str(event.get("started_at") or event_id).replace(":", "\\:").replace("'", "")
    title_text = str(event.get("display_title") or event.get("type") or "Surveillance event").replace(":", "\\:").replace("'", "")
    command = [
        "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
        "-f", "lavfi",
        "-i",
        (
            f"color=c=black:s={width}x360:d=3,"
            f"drawtext=text='{timestamp_text}':x=24:y=24:fontsize=22:fontcolor=white,"
            f"drawtext=text='{title_text}':x=24:y=72:fontsize=28:fontcolor=white,"
            f"drawtext=text='{event_id}':x=24:y=124:fontsize=18:fontcolor=white"
        ),
        "-loop", "0",
        str(output_path),
    ]
    result = core.run_command(command, timeout=60)
    if result.returncode == 0 and output_path.exists() and output_path.stat().st_size > 1024:
        return {"success": True, "file": str(output_path), "type": "animation", "fallback": True}
    return {
        "success": False,
        "message": (result.stderr.decode("utf-8", errors="ignore") or "Status preview GIF generation failed.")[:1000],
    }


def build_event_preview_gif(event: Dict[str, object]) -> Dict[str, object]:
    core = _core()
    if not core.PREVIEW_GIF_ENABLED:
        return {"success": False, "message": "Preview GIF is disabled."}

    event_id = str(event.get("id") or "event")
    video_path, audio_path = core.representative_media_paths(event)
    video_exists = bool(video_path and video_path.exists())
    audio_exists = bool(audio_path and audio_path.exists())

    preview_dir = core.PREVIEWS_DIR / event_id
    preview_dir.mkdir(parents=True, exist_ok=True)
    output_path = preview_dir / "preview.gif"
    face_event = int(event.get("face_count", 0)) > 0
    width = max(360, 720 if face_event else core.PREVIEW_GIF_WIDTH)
    fps = max(2, core.PREVIEW_GIF_FPS)
    speed = max(1.0, core.PREVIEW_GIF_SPEED)
    max_seconds = max(5, core.PREVIEW_GIF_MAX_SOURCE_SECONDS)
    timestamp_text = str(event.get("representative_at") or event.get("started_at") or event_id).replace(":", "\\:")

    crop_filter = detect_face_crop_filter(video_path) if face_event and video_exists and video_path else ""
    video_chain = f"trim=duration={max_seconds},setpts=PTS/{speed},fps={fps},"
    if crop_filter:
        video_chain += f"{crop_filter},"
    video_chain += (
        f"scale={width}:-1:flags=lanczos,"
        f"drawtext=text='{timestamp_text}  +%{{pts\\:hms}}':x=8:y=8:fontsize=20:"
        "fontcolor=white:box=1:boxcolor=black@0.55"
    )

    if video_exists and audio_exists and video_path and audio_path:
        filter_complex = (
            f"[0:v]{video_chain}[v];"
            f"[1:a]atrim=duration={max_seconds},showspectrum=s={width}x{core.PREVIEW_GIF_AUDIO_HEIGHT}:"
            "mode=combined:color=intensity:scale=log:slide=scroll,"
            f"setpts=PTS/{speed},format=rgba[a];"
            "[v][a]vstack=inputs=2:shortest=1,split[s0][s1];"
            "[s0]palettegen=max_colors=96[p];[s1][p]paletteuse=dither=bayer"
        )
        command = [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-i", str(video_path),
            "-i", str(audio_path),
            "-filter_complex", filter_complex,
            "-loop", "0",
            str(output_path),
        ]
    elif video_exists and video_path:
        video_filter = (
            f"{video_chain},"
            "split[s0][s1];[s0]palettegen=max_colors=96[p];[s1][p]paletteuse=dither=bayer"
        )
        command = [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-i", str(video_path),
            "-filter_complex", video_filter,
            "-loop", "0",
            str(output_path),
        ]
    elif audio_exists and audio_path:
        filter_complex = (
            f"[0:a]atrim=duration={max_seconds},showspectrum=s={width}x{max(core.PREVIEW_GIF_AUDIO_HEIGHT * 3, 240)}:"
            "mode=combined:color=intensity:scale=log:slide=scroll,"
            f"setpts=PTS/{speed},format=rgba,"
            f"drawtext=text='{timestamp_text}  audio event':x=8:y=8:fontsize=20:"
            "fontcolor=white:box=1:boxcolor=black@0.55,"
            "split[s0][s1];[s0]palettegen=max_colors=96[p];[s1][p]paletteuse=dither=bayer"
        )
        command = [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-i", str(audio_path),
            "-filter_complex", filter_complex,
            "-loop", "0",
            str(output_path),
        ]
    else:
        title_text = str(event.get("display_title") or event.get("type") or "Surveillance event").replace(":", "\\:").replace("'", "")
        video_filter = (
            f"color=c=black:s={width}x360:d=3,"
            f"drawtext=text='{timestamp_text}':x=24:y=24:fontsize=22:fontcolor=white,"
            f"drawtext=text='{title_text}':x=24:y=72:fontsize=28:fontcolor=white"
        )
        command = [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "lavfi",
            "-i", video_filter,
            "-loop", "0",
            str(output_path),
        ]

    result = core.run_command(command, timeout=180)
    if result.returncode != 0 or not output_path.exists() or output_path.stat().st_size < 1024:
        fallback = build_status_preview_gif(event, output_path, width)
        if fallback.get("success"):
            return fallback
        return {
            "success": False,
            "message": (result.stderr.decode("utf-8", errors="ignore") or fallback.get("message") or "Preview GIF generation failed.")[:1000],
        }
    return {
        "success": True,
        "file": str(output_path),
        "type": "animation",
        "speed": speed,
        "fps": fps,
        "seconds": min(max_seconds, int(event.get("duration_seconds") or max_seconds)),
        "source_segment_id": event.get("representative_segment_id"),
    }


def event_preview(event: Dict[str, object]) -> Dict[str, object]:
    return build_event_preview_gif(event)


def resolve_event_preview(event: Dict[str, object]) -> Optional[str]:
    core = _core()
    preview = str(event.get("preview_gif") or "").strip()
    if preview and Path(preview).exists():
        return preview
    event_id = str(event.get("id") or "").strip()
    if event_id:
        candidate = core.PREVIEWS_DIR / event_id / "preview.gif"
        if candidate.exists():
            return str(candidate)
    return None

