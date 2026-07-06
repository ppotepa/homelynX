from __future__ import annotations

import time
from typing import Dict, List, Optional

from .events import detect_coord_events, detect_coord_places
from .geo import _coord_avg, haversine_m, summarize_locations, timeline_points
from .time_utils import _coord_local_date, _coord_seconds_between, utc_now

def coord_report_days(points: List[Dict[str, object]], events: List[Dict[str, object]]) -> List[Dict[str, object]]:
    grouped: Dict[str, List[Dict[str, object]]] = {}
    for point in points:
        key = _coord_local_date(point.get('received_at') or point.get('recorded_at'))
        if key:
            grouped.setdefault(key, []).append(point)
    days = []
    for key, items in grouped.items():
        distance = 0.0
        for prev, curr in zip(items, items[1:]):
            distance += haversine_m(float(prev['lat']), float(prev['lon']), float(curr['lat']), float(curr['lon']))
        duration = _coord_seconds_between(items[0].get('received_at'), items[-1].get('received_at'))
        avg_accuracy = _coord_avg([item.get('accuracy_m') for item in items])
        event_count = len([event for event in events if _coord_local_date(event.get('start_at')) == key])
        days.append({
            'date': key,
            'points': len(items),
            'distance_m': round(distance, 1),
            'duration_s': duration,
            'avg_accuracy_m': round(avg_accuracy, 1) if avg_accuracy is not None else None,
            'event_count': event_count,
        })
    days.sort(key=lambda item: item['date'], reverse=True)
    return days
def build_coord_report(records: List[Dict[str, object]], scope_label: str, all_records: Optional[List[Dict[str, object]]] = None) -> Dict[str, object]:
    source_records = all_records or records
    points = timeline_points(source_records)
    events = detect_coord_events(points)
    places = detect_coord_places(events)
    days = coord_report_days(points, events)
    summary = summarize_locations(records)
    all_summary = summarize_locations(source_records)
    accuracies = [point.get('accuracy_m') for point in points]
    avg_accuracy = _coord_avg(accuracies)
    providers: Dict[str, int] = {}
    for point in points:
        provider = str(point.get('provider') or 'unknown')
        providers[provider] = providers.get(provider, 0) + 1
    return {
        'meta': {
            'title': 'Coord Intelligence',
            'generated_at': utc_now(),
            'timezone': time.tzname[0] if time.tzname else 'local',
            'version': 'single-file',
            'range_label': scope_label,
        },
        'summary': {
            'points': int(all_summary.get('count') or 0),
            'selected_points': int(summary.get('count') or 0),
            'events': len(events),
            'stops': len([event for event in events if event.get('type') == 'stop']),
            'movements': len([event for event in events if event.get('type') == 'movement']),
            'quality_events': len([event for event in events if event.get('type') in {'low_accuracy', 'gps_gap'}]),
            'places': len(places),
            'interesting': len([event for event in events if 'interesting' in (event.get('tags') or [])]),
            'duration_s': float(all_summary.get('duration_seconds') or 0.0),
            'distance_m': round(float(all_summary.get('distance_m') or 0.0), 1),
            'avg_accuracy_m': round(avg_accuracy, 1) if avg_accuracy is not None else None,
            'providers': providers,
            'first_at': (all_summary.get('first') or {}).get('received_at') if all_summary.get('first') else None,
            'last_at': (all_summary.get('last') or {}).get('received_at') if all_summary.get('last') else None,
            'range_label': scope_label,
        },
        'days': days,
        'points': points,
        'events': events,
        'places': places,
        'settings': {
            'privacy_note': 'Static local report. GPS data is embedded in this file. Map tiles may be requested from OpenStreetMap when the map loads.',
        },
    }
