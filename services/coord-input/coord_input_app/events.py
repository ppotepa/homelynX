from __future__ import annotations

from typing import Dict, List, Optional

from .geo import _coord_avg, _coord_center, haversine_m
from .time_utils import _coord_seconds_between, format_duration_text

def _coord_event(
    event_id: str,
    event_type: str,
    title: str,
    points: List[Dict[str, object]],
    distance_m: float = 0.0,
    tags: Optional[List[str]] = None,
    severity: str = 'normal',
    confidence: float = 0.7,
    subtitle: str = '',
) -> Dict[str, object]:
    start = points[0] if points else {}
    end = points[-1] if points else start
    duration_s = _coord_seconds_between(start.get('received_at') or start.get('recorded_at'), end.get('received_at') or end.get('recorded_at'))
    center = _coord_center(points)
    avg_accuracy = _coord_avg([point.get('accuracy_m') for point in points])
    return {
        'id': event_id,
        'type': event_type,
        'title': title,
        'subtitle': subtitle,
        'start_at': start.get('received_at') or start.get('recorded_at'),
        'end_at': end.get('received_at') or end.get('recorded_at'),
        'duration_s': duration_s,
        'distance_m': round(float(distance_m or 0.0), 1),
        'center': center,
        'point_indexes': [int(point.get('index') or 0) for point in points],
        'point_count': len(points),
        'avg_accuracy_m': round(avg_accuracy, 1) if avg_accuracy is not None else None,
        'confidence': max(0.0, min(1.0, float(confidence))),
        'severity': severity,
        'tags': tags or [],
    }
def detect_coord_events(points: List[Dict[str, object]]) -> List[Dict[str, object]]:
    if not points:
        return []
    ordered = sorted(points, key=lambda item: str(item.get('received_at') or item.get('recorded_at') or ''))
    events: List[Dict[str, object]] = []
    counter = 1

    def next_id(prefix: str) -> str:
        nonlocal counter
        value = f'{prefix}-{counter:04d}'
        counter += 1
        return value

    # Stops: compact clusters where the user stayed around the same center.
    i = 0
    while i < len(ordered):
        cluster = [ordered[i]]
        anchor = ordered[i]
        j = i + 1
        while j < len(ordered):
            distance = haversine_m(float(anchor['lat']), float(anchor['lon']), float(ordered[j]['lat']), float(ordered[j]['lon']))
            if distance > 120:
                break
            cluster.append(ordered[j])
            j += 1
        duration = _coord_seconds_between(cluster[0].get('received_at'), cluster[-1].get('received_at'))
        if len(cluster) >= 3 and duration >= 8 * 60:
            tags = ['stop']
            if duration >= 20 * 60:
                tags.append('interesting')
            events.append(_coord_event(next_id('stop'), 'stop', 'Stop detected', cluster, 0.0, tags, 'important' if 'interesting' in tags else 'normal', 0.82))
            i = max(j, i + 1)
        else:
            i += 1

    # Movement: consecutive jumps large enough to be real travel.
    moving: List[Dict[str, object]] = []
    moving_distance = 0.0
    for prev, curr in zip(ordered, ordered[1:]):
        distance = haversine_m(float(prev['lat']), float(prev['lon']), float(curr['lat']), float(curr['lon']))
        threshold = max(45.0, float(curr.get('accuracy_m') or 0.0) * 1.8)
        is_move = distance >= threshold
        if is_move:
            if not moving:
                moving = [prev]
            moving.append(curr)
            moving_distance += distance
        elif moving:
            duration = _coord_seconds_between(moving[0].get('received_at'), moving[-1].get('received_at'))
            if moving_distance >= 250 or duration >= 5 * 60:
                events.append(_coord_event(next_id('move'), 'movement', 'Movement segment', moving, moving_distance, ['movement'], 'normal', 0.76))
            moving = []
            moving_distance = 0.0
    if moving:
        duration = _coord_seconds_between(moving[0].get('received_at'), moving[-1].get('received_at'))
        if moving_distance >= 250 or duration >= 5 * 60:
            events.append(_coord_event(next_id('move'), 'movement', 'Movement segment', moving, moving_distance, ['movement'], 'normal', 0.76))

    # Quality events: low accuracy clusters and GPS gaps.
    low: List[Dict[str, object]] = []
    for point in ordered:
        accuracy = point.get('accuracy_m')
        is_low = accuracy is not None and float(accuracy) >= 80
        if is_low:
            low.append(point)
        elif low:
            if len(low) >= 2:
                events.append(_coord_event(next_id('qual'), 'low_accuracy', 'Low GPS accuracy', low, 0.0, ['quality'], 'normal', 0.9))
            low = []
    if len(low) >= 2:
        events.append(_coord_event(next_id('qual'), 'low_accuracy', 'Low GPS accuracy', low, 0.0, ['quality'], 'normal', 0.9))

    for prev, curr in zip(ordered, ordered[1:]):
        gap = _coord_seconds_between(prev.get('received_at'), curr.get('received_at'))
        if gap >= 30 * 60:
            events.append(_coord_event(next_id('gap'), 'gps_gap', 'GPS data gap', [prev, curr], 0.0, ['quality'], 'normal', 0.95, f'No points for {format_duration_text(gap)}'))

    if not events and ordered:
        events.append(_coord_event(next_id('sample'), 'sample', 'Track sample', ordered[: min(5, len(ordered))], 0.0, ['sample'], 'normal', 0.5))

    events.sort(key=lambda item: str(item.get('start_at') or ''))
    return events
def detect_coord_places(events: List[Dict[str, object]]) -> List[Dict[str, object]]:
    places: List[Dict[str, object]] = []
    for event in events:
        if event.get('type') != 'stop' or not event.get('center'):
            continue
        center = event['center']
        matched = None
        for place in places:
            distance = haversine_m(float(center['lat']), float(center['lon']), float(place['center']['lat']), float(place['center']['lon']))
            if distance <= 180:
                matched = place
                break
        if not matched:
            matched = {
                'id': f'place-{len(places) + 1:03d}',
                'label': f'Place {len(places) + 1}',
                'center': center,
                'visit_count': 0,
                'total_duration_s': 0.0,
                'point_count': 0,
                'event_ids': [],
            }
            places.append(matched)
        matched['visit_count'] = int(matched['visit_count']) + 1
        matched['total_duration_s'] = float(matched['total_duration_s']) + float(event.get('duration_s') or 0.0)
        matched['point_count'] = int(matched['point_count']) + int(event.get('point_count') or 0)
        matched['event_ids'].append(event.get('id'))
    return places
