"""Local TTS API using Piper with lightweight language detection."""

from __future__ import annotations

import asyncio
import os
import shutil
import subprocess
import time
import uuid
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, List, Optional
from urllib.request import urlretrieve

from fastapi import FastAPI
from pydantic import BaseModel, Field
from lingua import Language, LanguageDetectorBuilder


DATA_DIR = Path(os.getenv("TTS_DATA_DIR", "/data"))
MODEL_DIR = Path(os.getenv("TTS_MODEL_DIR", str(DATA_DIR / "models")))
OUTPUT_DIR = Path(os.getenv("TTS_OUTPUT_DIR", str(DATA_DIR / "output")))
PLAYBACK_ENABLED = os.getenv("TTS_PLAYBACK_ENABLED", "false").lower() == "true"
PLAYBACK_BACKEND = os.getenv("TTS_PLAYBACK_BACKEND", "auto").strip().lower() or "auto"
PLAYBACK_DEVICE = os.getenv("TTS_PLAYBACK_DEVICE", "default").strip() or "default"
PULSE_SINK = os.getenv("TTS_PULSE_SINK", "").strip()
PIPEWIRE_TARGET = os.getenv("TTS_PIPEWIRE_TARGET", "").strip()
DEFAULT_LANGUAGE = os.getenv("TTS_DEFAULT_LANGUAGE", "pl").lower()
MAX_TEXT_CHARS = int(os.getenv("TTS_MAX_TEXT_CHARS", "1000"))

VOICE_URLS = {
    "pl": {
        "name": os.getenv("TTS_PL_VOICE_NAME", "pl_PL-meski_wg_glos-medium"),
        "model": os.getenv(
            "TTS_PL_MODEL_URL",
            "https://huggingface.co/WitoldG/polish_piper_models/resolve/main/pl_PL-meski_wg_glos-medium.onnx",
        ),
        "config": os.getenv(
            "TTS_PL_CONFIG_URL",
            "https://huggingface.co/WitoldG/polish_piper_models/resolve/main/pl_PL-meski_wg_glos-medium.onnx.json",
        ),
    },
    "en": {
        "name": "en_US-lessac-medium",
        "model": os.getenv(
            "TTS_EN_MODEL_URL",
            "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx",
        ),
        "config": os.getenv(
            "TTS_EN_CONFIG_URL",
            "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx.json",
        ),
    },
}

LANGUAGE_MAP = {
    Language.POLISH: "pl",
    Language.ENGLISH: "en",
}

DETECTOR = LanguageDetectorBuilder.from_languages(Language.POLISH, Language.ENGLISH).build()
app = FastAPI(title="Home TTS Service", version="1.0.0")


class SpeakRequest(BaseModel):
    text: str = Field(min_length=1)
    language: str = "auto"
    play: bool = False
    voice: Optional[str] = None


class SpeakResponse(BaseModel):
    success: bool
    language: str
    voice: str
    file: Optional[str] = None
    played: bool = False
    duration_ms: int = 0
    message: str = ""


@dataclass
class VoicePaths:
    language: str
    name: str
    model: Path
    config: Path


def ensure_dirs() -> None:
    MODEL_DIR.mkdir(parents=True, exist_ok=True)
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)


def voice_paths(language: str) -> VoicePaths:
    voice = VOICE_URLS.get(language) or VOICE_URLS[DEFAULT_LANGUAGE]
    name = voice["name"]
    return VoicePaths(
        language=language,
        name=name,
        model=MODEL_DIR / f"{name}.onnx",
        config=MODEL_DIR / f"{name}.onnx.json",
    )


def ensure_voice(language: str) -> VoicePaths:
    ensure_dirs()
    paths = voice_paths(language)
    voice = VOICE_URLS[paths.language]

    if not paths.model.exists():
        urlretrieve(voice["model"], paths.model)
    if not paths.config.exists():
        urlretrieve(voice["config"], paths.config)

    return paths


def detect_language(text: str, requested_language: str) -> str:
    requested = requested_language.lower().strip()
    if requested and requested != "auto":
        return requested if requested in VOICE_URLS else DEFAULT_LANGUAGE

    detected = DETECTOR.detect_language_of(text)
    return LANGUAGE_MAP.get(detected, DEFAULT_LANGUAGE)


def synthesize(text: str, voice: VoicePaths, output_file: Path) -> None:
    command = [
        "piper",
        "--model",
        str(voice.model),
        "--config",
        str(voice.config),
        "--output_file",
        str(output_file),
    ]
    subprocess.run(command, input=text.encode("utf-8"), check=True)


def resolve_playback_device() -> str:
    """Resolve configured playback device to an ALSA device name."""
    if PLAYBACK_DEVICE.lower() in {"", "default"}:
        return "default"

    try:
        listed = subprocess.run(["aplay", "-l"], capture_output=True, text=True, check=False)
    except Exception:
        return "default"

    pattern = PLAYBACK_DEVICE.lower()
    current_card = None
    for line in listed.stdout.splitlines():
        lowered = line.lower()
        if line.startswith("card "):
            # Example: card 2: Device [JBL Go 4], device 0: USB Audio [USB Audio]
            parts = line.split(":", 1)
            if parts:
                current_card = parts[0].replace("card", "").strip()
        if current_card and pattern in lowered:
            device = "0"
            marker = "device "
            if marker in lowered:
                device_part = lowered.split(marker, 1)[1].split(":", 1)[0].strip()
                if device_part.isdigit():
                    device = device_part
            return f"hw:{current_card},{device}"

    return PLAYBACK_DEVICE


def list_playback_devices() -> List[str]:
    """Return a compact list of ALSA playback devices."""
    try:
        listed = subprocess.run(["aplay", "-l"], capture_output=True, text=True, check=False)
    except Exception:
        return []

    return [line.strip() for line in listed.stdout.splitlines() if line.startswith("card ")][:20]


def available_playback_players() -> List[str]:
    """Return available playback commands in preference order."""
    players = []
    pulse_socket = pulse_server_socket_exists()
    for player in ["pw-play", "paplay", "aplay"]:
        if player in {"pw-play", "paplay"} and not pulse_socket:
            continue
        if shutil.which(player):
            players.append(player)
    return players


def pulse_server_socket_exists() -> bool:
    """Return true when the configured PulseAudio/PipeWire socket exists."""
    pulse_server = os.getenv("PULSE_SERVER", "").strip()
    if pulse_server.startswith("unix:"):
        return Path(pulse_server.replace("unix:", "", 1)).is_socket()

    runtime_dir = os.getenv("XDG_RUNTIME_DIR", "").strip()
    if runtime_dir:
        return (Path(runtime_dir) / "pulse/native").is_socket()

    return False


def start_player(command: List[str]) -> bool:
    """Start an audio player and reject commands that fail immediately."""
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        return_code = process.wait(timeout=0.25)
    except subprocess.TimeoutExpired:
        return True

    return return_code == 0


def play_audio(path: Path) -> bool:
    if not PLAYBACK_ENABLED:
        return False

    players = available_playback_players()
    if not players:
        return False

    if PLAYBACK_BACKEND != "auto":
        players = [player for player in players if player == PLAYBACK_BACKEND]

    for player in players:
        try:
            command = [player]
            if player == "aplay":
                device = resolve_playback_device()
                if device:
                    command.extend(["-D", device])
            elif player == "paplay" and PULSE_SINK:
                command.extend(["--device", PULSE_SINK])
            elif player == "pw-play" and PIPEWIRE_TARGET:
                command.extend(["--target", PIPEWIRE_TARGET])

            command.append(str(path))
            if start_player(command):
                return True
        except Exception:
            continue

    return False


@app.get("/health")
def health() -> Dict[str, object]:
    return {
        "ok": True,
        "default_language": DEFAULT_LANGUAGE,
        "playback_enabled": PLAYBACK_ENABLED,
        "playback_backend": PLAYBACK_BACKEND,
        "playback_players": available_playback_players(),
        "pulse_server_socket": pulse_server_socket_exists(),
        "playback_device": PLAYBACK_DEVICE,
        "pulse_sink": PULSE_SINK or "default",
        "pipewire_target": PIPEWIRE_TARGET or "default",
        "resolved_playback_device": resolve_playback_device(),
        "playback_devices": list_playback_devices(),
        "voices": list(VOICE_URLS.keys()),
    }


@app.get("/voices")
def voices() -> Dict[str, List[Dict[str, str]]]:
    return {
        "voices": [
            {"language": language, "name": data["name"]}
            for language, data in VOICE_URLS.items()
        ]
    }


@app.post("/speak", response_model=SpeakResponse)
async def speak(request: SpeakRequest) -> SpeakResponse:
    started_at = time.monotonic()
    text = request.text.strip()
    if len(text) > MAX_TEXT_CHARS:
        return SpeakResponse(
            success=False,
            language="unknown",
            voice="none",
            message=f"Text is too long. Max {MAX_TEXT_CHARS} characters.",
        )

    language = detect_language(text, request.language)
    voice = ensure_voice(language)
    output_file = OUTPUT_DIR / f"tts-{uuid.uuid4().hex}.wav"

    try:
        await asyncio.to_thread(synthesize, text, voice, output_file)
    except Exception as exc:
        return SpeakResponse(
            success=False,
            language=language,
            voice=voice.name,
            duration_ms=round((time.monotonic() - started_at) * 1000),
            message=f"TTS synthesis failed: {exc}",
        )

    played = play_audio(output_file) if request.play else False
    return SpeakResponse(
        success=True,
        language=language,
        voice=voice.name,
        file=str(output_file),
        played=played,
        duration_ms=round((time.monotonic() - started_at) * 1000),
        message="ok",
    )
