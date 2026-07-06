from __future__ import annotations

import json
import shutil


def _core():
    from . import core

    return core


def analyzer_worker() -> None:
    core = _core()
    while not core.stop_event.is_set():
        try:
            segment = core.segment_queue.get(timeout=1)
        except core.queue.Empty:
            continue
        if not segment.audio_path:
            speech_ratio, peak, rms = 0.0, 0.0, 0.0
        try:
            if segment.audio_path:
                speech_ratio, peak, rms = core.analyze_audio(segment.audio_path)
            motion, face_count, person_count, motion_ratio, annotated_snapshot_path = core.analyze_visual(segment.snapshot_path)
            result = core.AnalysisResult(
                segment=segment,
                speech=speech_ratio >= core.SPEECH_RATIO_THRESHOLD,
                loud=peak >= core.LOUD_PEAK_THRESHOLD,
                noise_spike=peak >= (core.LOUD_PEAK_THRESHOLD * core.NOISE_SPIKE_MULTIPLIER),
                motion=motion,
                face_count=face_count,
                person_count=person_count,
                motion_ratio=motion_ratio,
                annotated_snapshot_path=annotated_snapshot_path,
                speech_ratio=speech_ratio,
                peak=peak,
                rms=rms,
            )
            core.clear_error()
            core.analysis_queue.put(result)
        except Exception as exc:
            core.set_error(f"Analyzer failed: {exc}")


def event_loop() -> None:
    core = _core()
    while not core.stop_event.is_set():
        try:
            result = core.analysis_queue.get(timeout=1)
        except core.queue.Empty:
            now_ts = core.time.time()
            if core.last_error and not core.device_alert_active and core.device_alert_due(now_ts):
                core.device_alert_active = True
                core.last_device_alert_at = now_ts
                system_event = core.create_system_event("device_error", f"Recorder issue detected: {core.last_error}")
                with core.state_lock:
                    core.events.append(system_event)
                    del core.events[:-core.MAX_EVENTS_MEMORY]
                core.enqueue_event_preview_notification(system_event)
            elif not core.last_error and core.device_alert_active and core.device_alert_due(now_ts):
                core.device_alert_active = False
                core.last_device_alert_at = now_ts
                system_event = core.create_system_event("device_recovered", "Recorder recovered and segments are flowing again.")
                with core.state_lock:
                    core.events.append(system_event)
                    del core.events[:-core.MAX_EVENTS_MEMORY]
                core.enqueue_event_preview_notification(system_event)
            continue

        if core.device_alert_active and not core.last_error:
            core.device_alert_active = False

        is_trigger = core.result_triggers_event(result)
        has_context = core.result_has_context(result)

        if core.active_event:
            if is_trigger or has_context:
                core.update_event(core.active_event, result)
            if is_trigger:
                core.last_event_update = result.segment.timestamp
            elif result.segment.timestamp - core.last_event_update > core.EVENT_GAP_SECONDS:
                finalized_event = core.finalize_event(core.active_event, core.last_event_update)
                with core.state_lock:
                    core.events.append(finalized_event)
                    del core.events[:-core.MAX_EVENTS_MEMORY]
                core.active_event = None
            continue

        if is_trigger:
            core.active_event = core.create_event(result)
            core.last_event_update = result.segment.timestamp


def cleanup_loop() -> None:
    core = _core()
    while not core.stop_event.is_set():
        segments_cutoff = core.time.time() - core.RETENTION_HOURS * 3600
        events_cutoff = core.time.time() - core.EVENT_RETENTION_DAYS * 86400

        for root, cutoff in [
            (core.SEGMENTS_DIR, segments_cutoff),
            (core.SNAPSHOTS_DIR, segments_cutoff),
            (core.CLIPS_DIR, segments_cutoff),
            (core.PREVIEWS_DIR, segments_cutoff),
        ]:
            if root.exists():
                for path in root.rglob("*"):
                    try:
                        if path.is_file() and path.stat().st_mtime < cutoff:
                            path.unlink()
                    except Exception:
                        pass

        if core.EVENTS_DIR.exists():
            for event_file in core.EVENTS_DIR.glob("**/event.json"):
                try:
                    payload = json.loads(event_file.read_text(encoding="utf-8"))
                    ended_at_ts = float(payload.get("ended_at_ts") or payload.get("updated_at_ts") or 0.0)
                    event_dir = event_file.parent
                    if ended_at_ts and ended_at_ts < events_cutoff:
                        shutil.rmtree(event_dir, ignore_errors=True)
                except Exception:
                    continue
        core.cleanup_jobs()
        core.stop_event.wait(600)


def enrichment_worker() -> None:
    core = _core()
    while not core.stop_event.is_set():
        try:
            event_id = core.enrichment_queue.get(timeout=1)
        except core.queue.Empty:
            continue
        try:
            event = core.load_event(str(event_id))
            if event and str(event.get("transcript_status") or "") not in {"queued", "running"}:
                core.enqueue_job("llm_summary", str(event_id), int(event.get("priority_score") or 50))
        finally:
            core.enrichment_queue.task_done()


def start_workers() -> None:
    core = _core()
    core.threading.Thread(target=core.recorder_loop, daemon=True).start()
    core.threading.Thread(target=core.segment_watcher_loop, daemon=True).start()
    for _ in range(max(1, core.ANALYZER_WORKERS)):
        core.threading.Thread(target=analyzer_worker, daemon=True).start()
    for index in range(max(1, core.JOB_WORKERS)):
        core.threading.Thread(target=core.job_worker, args=(index,), daemon=True).start()
    core.threading.Thread(target=enrichment_worker, daemon=True).start()
    core.threading.Thread(target=event_loop, daemon=True).start()
    core.threading.Thread(target=cleanup_loop, daemon=True).start()
