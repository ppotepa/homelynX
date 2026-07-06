"""Lightweight local surveillance recorder and audio event detector."""

from __future__ import annotations

import json
import math
import os
import queue
import shutil
import sqlite3
import subprocess
import threading
import time
import uuid
import wave
import re
from html import escape
from dataclasses import dataclass, asdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Optional, Tuple

try:
    import cv2
except ModuleNotFoundError:  # pragma: no cover - import fallback for local tooling
    cv2 = None

try:
    import numpy as np
except ModuleNotFoundError:  # pragma: no cover - import fallback for local tooling
    np = None

try:
    import requests
except ModuleNotFoundError:  # pragma: no cover - import fallback for local tooling
    requests = None

try:
    from faster_whisper import WhisperModel
except ModuleNotFoundError:  # pragma: no cover - import fallback for local tooling
    class WhisperModel:  # type: ignore[no-redef]
        pass

try:
    from fastapi import FastAPI, Query
except ModuleNotFoundError:  # pragma: no cover - import fallback for local tooling
    def Query(default=None, **kwargs):  # type: ignore[no-redef]
        return default

    class FastAPI:  # type: ignore[no-redef]
        def __init__(self, *args, **kwargs):
            pass

        def get(self, *args, **kwargs):
            def decorator(func):
                return func

            return decorator

try:
    import uvicorn
except ModuleNotFoundError:  # pragma: no cover - import fallback for local tooling
    class _Uvicorn:
        @staticmethod
        def run(*args, **kwargs):
            raise ModuleNotFoundError("uvicorn")

    uvicorn = _Uvicorn()


DATA_DIR = Path(os.getenv("SURV_DATA_DIR", "/data"))
SEGMENTS_DIR = DATA_DIR / "segments"
EVENTS_DIR = DATA_DIR / "events"
SNAPSHOTS_DIR = DATA_DIR / "snapshots"
CLIPS_DIR = DATA_DIR / "clips"
PREVIEWS_DIR = DATA_DIR / "previews"
JOBS_DB = DATA_DIR / "jobs.sqlite3"
CAMERA_DEVICE = os.getenv("SURV_CAMERA_DEVICE", "/dev/video0")
AUDIO_DEVICE = os.getenv("SURV_AUDIO_DEVICE", "default")
AUDIO_FILTER = os.getenv(
    "SURV_AUDIO_FILTER",
    "highpass=f=120,lowpass=f=7000,afftdn=nf=-25,alimiter=limit=0.85",
).strip()
SEGMENT_SECONDS = int(os.getenv("SURV_SEGMENT_SECONDS", "5"))
RECORDER_WATCHDOG_SECONDS = int(os.getenv("SURV_RECORDER_WATCHDOG_SECONDS", str(max(15, SEGMENT_SECONDS * 4))))
RECORDER_RESTART_DELAY_SECONDS = int(os.getenv("SURV_RECORDER_RESTART_DELAY_SECONDS", "3"))
SNAPSHOT_RAM_LIMIT_BYTES = int(os.getenv("SURV_SNAPSHOT_RAM_LIMIT_BYTES", str(1024 * 1024 * 1024)))
RETENTION_HOURS = int(os.getenv("SURV_RETENTION_HOURS", "24"))
EVENT_RETENTION_DAYS = int(os.getenv("SURV_EVENT_RETENTION_DAYS", "14"))
RECORD_VIDEO = os.getenv("SURV_RECORD_VIDEO", "true").lower() == "true"
RECORD_AUDIO = os.getenv("SURV_RECORD_AUDIO", "true").lower() == "true"
ANALYZER_WORKERS = int(os.getenv("SURV_ANALYZER_WORKERS", "2"))
SPEECH_RATIO_THRESHOLD = float(os.getenv("SURV_SPEECH_RATIO_THRESHOLD", "0.18"))
LOUD_PEAK_THRESHOLD = float(os.getenv("SURV_LOUD_PEAK_THRESHOLD", "0.45"))
EVENT_GAP_SECONDS = int(os.getenv("SURV_EVENT_GAP_SECONDS", "25"))
INCIDENT_GAP_SECONDS = int(os.getenv("SURV_INCIDENT_GAP_SECONDS", "90"))
PRE_ROLL_SEGMENTS = int(os.getenv("SURV_EVENT_PRE_ROLL_SEGMENTS", "1"))
POST_ROLL_SEGMENTS = int(os.getenv("SURV_EVENT_POST_ROLL_SEGMENTS", "1"))
TELEGRAM_BOT_TOKEN = os.getenv("SURV_TELEGRAM_BOT_TOKEN", "").strip() or os.getenv("TELEGRAM_BOT_TOKEN", "").strip()
TELEGRAM_NOTIFICATION_CHAT_ID = (
    os.getenv("SURV_TELEGRAM_CHAT_ID", "").strip()
    or os.getenv("TELEGRAM_NOTIFICATION_CHAT_ID", "").strip()
    or os.getenv("TELEGRAM_ADMIN_CHAT_ID", "").strip()
)
TELEGRAM_MIN_SEVERITY = os.getenv("SURV_TELEGRAM_MIN_SEVERITY", "medium").strip().lower()
NOTIFY_ENABLED = os.getenv("SURV_NOTIFY_ENABLED", "true").lower() == "true"
TRANSCRIBE_ENABLED = os.getenv("SURV_TRANSCRIBE_ENABLED", "true").lower() == "true"
TRANSCRIBE_MODEL = os.getenv("SURV_TRANSCRIBE_MODEL", "tiny")
TRANSCRIBE_LANGUAGE = os.getenv("SURV_TRANSCRIBE_LANGUAGE", "auto").strip().lower()
TRANSCRIBE_COMPUTE_TYPE = os.getenv("SURV_TRANSCRIBE_COMPUTE_TYPE", "int8")
TRANSCRIBE_BEAM_SIZE = int(os.getenv("SURV_TRANSCRIBE_BEAM_SIZE", "1"))
TRANSCRIBE_VAD = os.getenv("SURV_TRANSCRIBE_VAD", "true").lower() == "true"
TRANSCRIBE_VAD_MIN_SILENCE_MS = int(os.getenv("SURV_TRANSCRIBE_VAD_MIN_SILENCE_MS", "500"))
TRANSCRIBE_WORD_TIMESTAMPS = os.getenv("SURV_TRANSCRIBE_WORD_TIMESTAMPS", "false").lower() == "true"
TRANSCRIBE_CONDITION_ON_PREVIOUS_TEXT = os.getenv("SURV_TRANSCRIBE_CONDITION_ON_PREVIOUS_TEXT", "false").lower() == "true"
JOB_WORKERS = int(os.getenv("SURV_JOB_WORKERS", "1"))
JOB_MAX_ATTEMPTS = int(os.getenv("SURV_JOB_MAX_ATTEMPTS", "3"))
JOB_POLL_SECONDS = float(os.getenv("SURV_JOB_POLL_SECONDS", "1.0"))
JOB_DONE_RETENTION_HOURS = int(os.getenv("SURV_JOB_DONE_RETENTION_HOURS", "48"))
JOB_FAILED_RETENTION_HOURS = int(os.getenv("SURV_JOB_FAILED_RETENTION_HOURS", "168"))
PREVIEW_GIF_ENABLED = os.getenv("SURV_PREVIEW_GIF_ENABLED", "true").lower() == "true"
PREVIEW_GIF_SPEED = float(os.getenv("SURV_PREVIEW_GIF_SPEED", "5.0"))
PREVIEW_GIF_FPS = int(os.getenv("SURV_PREVIEW_GIF_FPS", "6"))
PREVIEW_GIF_WIDTH = int(os.getenv("SURV_PREVIEW_GIF_WIDTH", "480"))
PREVIEW_GIF_AUDIO_HEIGHT = int(os.getenv("SURV_PREVIEW_GIF_AUDIO_HEIGHT", "84"))
PREVIEW_GIF_MAX_SOURCE_SECONDS = int(os.getenv("SURV_PREVIEW_GIF_MAX_SOURCE_SECONDS", "180"))
OPERATOR_SUMMARY_DEFAULT_HOURS = int(os.getenv("SURV_OPERATOR_SUMMARY_DEFAULT_HOURS", "24"))
OPERATOR_SUMMARY_MAX_EVENTS = int(os.getenv("SURV_OPERATOR_SUMMARY_MAX_EVENTS", "120"))
OPERATOR_SUMMARY_MAX_TRANSCRIPT_CHARS = int(os.getenv("SURV_OPERATOR_SUMMARY_MAX_TRANSCRIPT_CHARS", "14000"))
OPERATOR_SUMMARY_TIMEOUT_SECONDS = int(os.getenv("SURV_OPERATOR_SUMMARY_TIMEOUT_SECONDS", "45"))
NOTIFY_MIN_INTERVAL_SECONDS = int(os.getenv("SURV_NOTIFY_MIN_INTERVAL_SECONDS", "45"))
NOTIFY_BACKLOG_SUPPRESS_SECONDS = int(os.getenv("SURV_NOTIFY_BACKLOG_SUPPRESS_SECONDS", "180"))
MAX_EVENTS_MEMORY = int(os.getenv("SURV_MAX_EVENTS_MEMORY", "100"))
RECENT_SEGMENT_BUFFER = int(os.getenv("SURV_RECENT_SEGMENT_BUFFER", "240"))
SPEECH_EXTENDED_SEGMENTS = int(os.getenv("SURV_SPEECH_EXTENDED_SEGMENTS", "2"))
NOISE_SPIKE_MULTIPLIER = float(os.getenv("SURV_NOISE_SPIKE_MULTIPLIER", "2.4"))
DEVICE_ALERT_COOLDOWN_SECONDS = int(os.getenv("SURV_DEVICE_ALERT_COOLDOWN_SECONDS", "300"))
MOTION_ENABLED = os.getenv("SURV_MOTION_ENABLED", "true").lower() == "true"
FACE_ENABLED = os.getenv("SURV_FACE_ENABLED", "true").lower() == "true"
PERSON_ENABLED = os.getenv("SURV_PERSON_ENABLED", "true").lower() == "true"
MOTION_DIFF_THRESHOLD = int(os.getenv("SURV_MOTION_DIFF_THRESHOLD", "18"))
MOTION_MIN_RATIO = float(os.getenv("SURV_MOTION_MIN_RATIO", "0.015"))
FACE_MIN_SIZE = int(os.getenv("SURV_FACE_MIN_SIZE", "64"))
PERSON_HOG_STRIDE = int(os.getenv("SURV_PERSON_HOG_STRIDE", "8"))
PERSON_MIN_CONFIDENCE = float(os.getenv("SURV_PERSON_MIN_CONFIDENCE", "0.45"))
EVENT_TRIGGER_KINDS = {
    item.strip().upper()
    for item in os.getenv("SURV_EVENT_TRIGGER_KINDS", "PERSON_DETECTION,FACE_DETECTION").split(",")
    if item.strip()
}
LLM_ENABLED = os.getenv("LLM_ENABLED", "false").lower() == "true"
LLM_HOST = os.getenv("LLM_HOST", "llm")
LLM_PORT = int(os.getenv("LLM_PORT", "11434"))
LLM_MODEL = os.getenv("LLM_MODEL", "qwen3:0.6b")
LLM_TIMEOUT_SECONDS = int(os.getenv("LLM_TIMEOUT_SECONDS", "20"))
LLM_MAX_TRANSCRIPT_CHARS = int(os.getenv("LLM_MAX_TRANSCRIPT_CHARS", "1600"))
LLM_SUMMARIES_ENABLED = os.getenv("SURV_LLM_SUMMARIES_ENABLED", "true").lower() == "true"
LLM_MIN_SEVERITY = os.getenv("SURV_LLM_MIN_SEVERITY", "medium").strip().lower()
LLM_BASE_URL = f"http://{LLM_HOST}:{LLM_PORT}"
LLM_AUDIT_URL = os.getenv("LLM_AUDIT_URL", "").strip()
LLM_AUDIT_TOKEN = os.getenv("LLM_AUDIT_TOKEN", "").strip()
LLM_SYSTEM_PROMPT = (
    "You are Homelynx Surveillance Analyst, a local privacy-preserving assistant for the home surveillance service. "
    "Use only provided event, incident, transcript, signal, and metric data. "
    "Do not invent identities, causes, locations, or actions. "
    "When asked for JSON, return only JSON. Keep operator notifications compact and factual."
)

app = FastAPI(title="Home Surveillance Service", version="1.0.0")
stop_event = threading.Event()
segment_queue: "queue.Queue[Segment]" = queue.Queue()
analysis_queue: "queue.Queue[AnalysisResult]" = queue.Queue()
enrichment_queue: "queue.Queue[str]" = queue.Queue()
recent_segments: List[Segment] = []
snapshot_cache: List[Dict[str, object]] = []
snapshot_cache_bytes = 0
events: List[Dict[str, object]] = []
state_lock = threading.Lock()
last_segment_at: Optional[str] = None
last_error: Optional[str] = None
recorder_process: Optional[subprocess.Popen] = None
recorder_state = "stopped"
recorder_restarts = 0
last_recorder_restart_at: Optional[str] = None
last_recorder_error: Optional[str] = None
active_event: Optional[Dict[str, object]] = None
last_event_update = 0.0
whisper_model: Optional[WhisperModel] = None
whisper_lock = threading.Lock()
job_lock = threading.Lock()
job_store_ready = False
device_alert_active = False
last_device_alert_at = 0.0
vision_lock = threading.Lock()
previous_motion_frame: Optional[np.ndarray] = None
face_cascade: Optional[cv2.CascadeClassifier] = None
person_hog = None
last_llm_error: Optional[str] = None
last_llm_enriched_at: Optional[str] = None
last_notification_at = 0.0

SEVERITY_RANK = {"low": 10, "medium": 50, "high": 75, "critical": 100}
EVENT_KIND_PRIORITY = {
    "PERSON_DETECTION": 500,
    "FACE_DETECTION": 400,
    "VOICE": 300,
    "LOUD": 200,
    "MOVEMENT": 100,
    "ACTIVITY": 10,
    "DEVICE_ERROR": 1000,
    "DEVICE_RECOVERED": 900,
}
KIND_TYPE_MAP = {
    "PERSON_DETECTION": "person_detected",
    "FACE_DETECTION": "face_detected",
    "VOICE": "speech",
    "LOUD": "loud",
    "MOVEMENT": "motion",
    "DEVICE_ERROR": "device_error",
    "DEVICE_RECOVERED": "device_recovered",
    "ACTIVITY": "activity",
}
TYPE_KIND_MAP = {
    "person_detected": "PERSON_DETECTION",
    "face_detected": "FACE_DETECTION",
    "speech": "VOICE",
    "speech_extended": "VOICE",
    "speech+loud": "VOICE",
    "loud": "LOUD",
    "noise_spike": "LOUD",
    "motion": "MOVEMENT",
    "device_error": "DEVICE_ERROR",
    "device_recovered": "DEVICE_RECOVERED",
    "activity": "ACTIVITY",
}
SURVEILLANCE_AUDIT_FEATURES = {
    "stt": "surveillance_stt",
    "llm_summary": "surveillance_llm_summary",
    "preview_gif": "surveillance_preview_gif",
    "send_notification": "surveillance_notification",
    "operator_summary": "surveillance_operator_summary",
}


@dataclass
class Segment:
    id: str
    started_at: str
    timestamp: float
    audio_path: Optional[str]
    video_path: Optional[str]
    snapshot_path: Optional[str] = None


@dataclass
class AnalysisResult:
    segment: Segment
    speech: bool
    loud: bool
    noise_spike: bool
    motion: bool
    face_count: int
    person_count: int
    motion_ratio: float
    annotated_snapshot_path: Optional[str]
    speech_ratio: float
    peak: float
    rms: float


from .events.classification import (
    analysis_result_record,
    analysis_result_score,
    apply_representative_result,
    classify_event,
    duration_label,
    event_type_for_event,
    kind_from_signals,
    merge_event_signals,
    normalize_event,
    normalize_event_kind,
    normalize_signals,
    representative_media_paths,
    representative_segment,
    result_has_activity,
    result_has_context,
    result_signals,
    result_triggers_event,
)
from .media.ffmpeg import concat_media, copy_event_file, existing_file, extract_audio_from_media, extract_snapshot_from_video, mux_video_audio, run_command, write_concat_list
from .media.clips import build_clip_from_segments, build_recent_clip, collect_incident_segments, event_clip, incident_clip, recent_clip
from .events.summaries import (
    build_digest,
    build_event_display_summary,
    build_incident_summary,
    build_incident_title,
    build_incidents,
    build_operator_summary_payload,
    build_priority_items,
    build_scene_description,
    build_stats,
    build_storage_stats,
    build_summary,
    compact_event_for_summary,
    directory_file_count,
    directory_size_bytes,
    directory_time_range,
    event_matches_filter,
    filter_events_by_hours,
)
from .events.repository import (
    cache_snapshot,
    capture_camera_snapshot,
    capture_live_snapshot,
    collect_incident_segments,
    event_directory,
    event_start_tuple,
    extract_event_snapshot_from_segments,
    latest_snapshot_file,
    latest_snapshot_from_memory,
    load_event,
    load_recent_events,
    resolve_event_preview,
    resolve_event_snapshot,
    segment_name,
    segment_timestamp_from_path,
    take_snapshot,
)
from .stt import event_transcript, get_whisper_model, transcribe_audio, write_event_transcript_files
from .llm import (
    audit_activity_call,
    audit_llm_call,
    call_llm_event_summary,
    compact_event_audit_context,
    enrich_event_with_llm,
    llm_status_payload,
    parse_llm_json,
    process_llm_job,
    process_operator_summary_job,
    should_enrich_with_llm,
    surveillance_audit_feature,
    surveillance_llm_status,
    surveillance_summary,
)
from .jobs.worker import job_worker, process_job, process_operator_summary_job, process_preview_gif_job, process_send_notification_job, process_stt_job
from .recorder.ffmpeg_recorder import append_daily_index, build_recorder_command, process_recorded_segment, recorder_loop, recorder_stale, stop_recorder_process
from .recorder.watcher import segment_watcher_loop
from .storage.jobs import cleanup_jobs, claim_next_job, complete_job, enqueue_job, ensure_job_store, fail_job, job_connection, job_max_attempts, job_stats
from .state import clear_error, ensure_dirs, set_error, shutdown_handler
from .telegram import create_system_event, device_alert_due, format_local_time, format_telegram_alert, format_telegram_analysis_update, format_telegram_photo_caption, format_transcript_language, format_transcript_quote, html_text, notify_telegram, notify_telegram_analysis_update, notify_telegram_preview, send_telegram_text_chunks, telegram_commands_block, event_ready_for_notification
from .analysis.audio import analyze_audio
from .analysis.vision import analyze_visual, cache_snapshot, get_face_cascade, get_person_hog
from .events.lifecycle import create_event, enqueue_event_preview_notification, enqueue_event_processing, finalize_event, prepare_event_processing, should_transcribe_event, update_event
from .events.repository import get_recent_segments_for_event, save_event
from .workers import analyzer_worker


def load_incident(incident_id: str):
    from .events.incidents import load_incident as _load_incident

    return _load_incident(incident_id)


def collect_incident_transcript(incident):
    from .events.incidents import collect_incident_transcript as _collect_incident_transcript

    return _collect_incident_transcript(incident)


def main() -> None:
    from .bootstrap import main as bootstrap_main

    bootstrap_main()


if __name__ == "__main__":
    main()
