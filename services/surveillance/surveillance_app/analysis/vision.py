from __future__ import annotations

from pathlib import Path
from typing import Optional

from ..events.repository import cache_snapshot


def _core():
    from .. import core

    return core


def get_face_cascade() -> Optional[object]:
    core = _core()
    if not core.FACE_ENABLED:
        return None
    with core.vision_lock:
        if core.face_cascade is None:
            cascade_path = Path(core.cv2.data.haarcascades) / "haarcascade_frontalface_default.xml"
            classifier = core.cv2.CascadeClassifier(str(cascade_path))
            core.face_cascade = classifier if not classifier.empty() else None
        return core.face_cascade


def get_person_hog():
    core = _core()
    if not core.PERSON_ENABLED:
        return None
    with core.vision_lock:
        if core.person_hog is None:
            hog = core.cv2.HOGDescriptor()
            hog.setSVMDetector(core.cv2.HOGDescriptor_getDefaultPeopleDetector())
            core.person_hog = hog
        return core.person_hog


def analyze_visual(snapshot_path: Optional[str]) -> tuple[bool, int, int, float, Optional[str]]:
    core = _core()
    if not snapshot_path or not Path(snapshot_path).exists():
        return False, 0, 0, 0.0, None

    color_original = core.cv2.imread(snapshot_path, core.cv2.IMREAD_COLOR)
    if color_original is None:
        return False, 0, 0, 0.0, None

    color_frame = core.cv2.resize(color_original, (640, 360))
    gray_frame = core.cv2.cvtColor(color_frame, core.cv2.COLOR_BGR2GRAY)
    motion_frame = core.cv2.GaussianBlur(gray_frame, (21, 21), 0)

    motion_ratio = 0.0
    motion = False
    with core.vision_lock:
        if core.previous_motion_frame is not None and core.MOTION_ENABLED:
            delta = core.cv2.absdiff(core.previous_motion_frame, motion_frame)
            threshold = core.cv2.threshold(delta, core.MOTION_DIFF_THRESHOLD, 255, core.cv2.THRESH_BINARY)[1]
            threshold = core.cv2.dilate(threshold, None, iterations=2)
            motion_ratio = float(core.np.count_nonzero(threshold)) / float(threshold.size)
            motion = motion_ratio >= core.MOTION_MIN_RATIO
        core.previous_motion_frame = motion_frame

    face_count = 0
    face_rects = []
    classifier = get_face_cascade()
    if classifier is not None:
        faces = classifier.detectMultiScale(
            gray_frame,
            scaleFactor=1.1,
            minNeighbors=5,
            minSize=(core.FACE_MIN_SIZE, core.FACE_MIN_SIZE),
        )
        face_rects = list(faces)
        face_count = len(faces)

    person_count = 0
    person_rects = []
    hog = get_person_hog()
    if hog is not None:
        rects, weights = hog.detectMultiScale(
            color_frame,
            winStride=(core.PERSON_HOG_STRIDE, core.PERSON_HOG_STRIDE),
            padding=(8, 8),
            scale=1.05,
        )
        for rect, weight in zip(rects, weights):
            if float(weight) >= core.PERSON_MIN_CONFIDENCE:
                person_rects.append(rect)
        person_count = len(person_rects)

    annotated_path = None
    if face_rects or person_rects:
        annotated = color_frame.copy()
        for x, y, w, h in face_rects:
            core.cv2.rectangle(annotated, (x, y), (x + w, y + h), (0, 255, 0), 3)
            core.cv2.putText(annotated, "face", (x, max(20, y - 8)), core.cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
        for x, y, w, h in person_rects:
            core.cv2.rectangle(annotated, (x, y), (x + w, y + h), (255, 128, 0), 3)
            core.cv2.putText(annotated, "person", (x, max(20, y - 8)), core.cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 128, 0), 2)
        source = Path(snapshot_path)
        annotated_file = source.with_name(f"{source.stem}-detected.jpg")
        if core.cv2.imwrite(str(annotated_file), annotated):
            annotated_path = str(annotated_file)

    return motion, face_count, person_count, motion_ratio, annotated_path
