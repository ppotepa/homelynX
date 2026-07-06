from __future__ import annotations

from typing import Dict, Optional

from ..core import load_recent_events
from .summaries import build_incidents


def load_incident(incident_id: str) -> Optional[Dict[str, object]]:
    for incident in build_incidents(load_recent_events(200)):
        if str(incident.get("id")) == incident_id:
            return incident
    return None


def collect_incident_transcript(incident: Dict[str, object]) -> str:
    parts = []
    for event in incident.get("events", []):
        transcript = str(event.get("transcript") or "").strip()
        if transcript:
            language = str(event.get("transcript_language") or "").strip()
            probability = float(event.get("transcript_language_probability") or 0.0)
            language_label = f" [{language} {probability:.0%}]" if language else ""
            parts.append(f"{event.get('id')}{language_label}: {transcript}")
    return "\n\n".join(parts).strip()
