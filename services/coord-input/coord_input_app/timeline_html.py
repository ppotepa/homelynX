from __future__ import annotations

import json
from datetime import datetime, timezone, timedelta
from html import escape
from typing import Dict, List, Optional

from .config import EXPORTS_DIR
from .geo import haversine_m, summarize_locations
from .llm_summary import timeline_llm_summary
from .reports import build_coord_report
from .storage import history_all, history_since
from .time_utils import canonical_range_label, local_time, parse_duration_to_seconds

def build_timeline_html(
    records: List[Dict[str, object]],
    scope_label: str,
    llm_summary: Optional[Dict[str, object]] = None,
    all_records: Optional[List[Dict[str, object]]] = None,
) -> str:
    report = build_coord_report(records, scope_label, all_records)
    if llm_summary:
        report['llm_summary'] = llm_summary
    report_json = json.dumps(report, ensure_ascii=False, separators=(',', ':')).replace('</', '<\\/')
    template = """<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Coord Intelligence</title>
  <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css">
  <style>
    :root {
      color-scheme: dark;
      --bg: #07090c;
      --panel: #0b0f14;
      --panel-2: #10161d;
      --panel-3: #151c24;
      --line: #26303a;
      --line-soft: #1c242d;
      --line-strong: #3a4652;
      --text: #e8edf0;
      --muted: #8b97a3;
      --muted-2: #687380;
      --none: #242a31;
      --none-2: #191f26;
      --low: #a7c8a6;
      --mid: #4d8d57;
      --high: #174f2b;
      --ink: #08100a;
      --blue: #7698cf;
      --yellow: #b4974a;
      --red: #c26060;
      --radius: 4px;
      --axis-h: 56px;
      --left-w: 318px;
      --right-w: 312px;
    }
    * { box-sizing: border-box; }
    html, body { margin: 0; min-height: 100%; }
    body {
      background: var(--bg);
      color: var(--text);
      font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      overflow: hidden;
      font-size: 13px;
    }
    button, input, select { font: inherit; }
    button { border: 0; color: inherit; background: none; cursor: pointer; }
    code { font-family: "Cascadia Mono", "SFMono-Regular", Consolas, monospace; }
    a { color: var(--low); text-decoration: none; }
    .top-axis { height: var(--axis-h); display: grid; grid-template-columns: 190px minmax(0, 1fr) 245px; background: #080c10; border-bottom: 1px solid var(--line); position: sticky; top: 0; z-index: 100; }
    .brand-compact { display: grid; align-content: center; gap: 1px; padding: 7px 10px; border-right: 1px solid var(--line); }
    .brand-compact b { font-size: 12px; letter-spacing: .05em; text-transform: uppercase; }
    .brand-compact span { color: var(--muted); font-size: 10.5px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .axis-center { min-width: 0; display: grid; grid-template-rows: 17px 1fr; }
    .axis-labels { height: 17px; display: flex; justify-content: space-between; align-items: center; padding: 0 8px; border-bottom: 1px solid var(--line-soft); color: var(--muted-2); font-size: 9.5px; letter-spacing: .08em; text-transform: uppercase; }
    .day-rail { display: grid; grid-auto-flow: column; grid-auto-columns: minmax(54px, 1fr); gap: 1px; overflow-x: auto; overflow-y: hidden; background: #151b22; scrollbar-width: thin; scrollbar-color: #3a4652 transparent; }
    .day-cell { height: 38px; min-width: 54px; padding: 4px 5px; display: grid; grid-template-rows: 13px 1fr; align-items: center; text-align: left; background: var(--none-2); color: var(--muted); border: 1px solid transparent; position: relative; }
    .day-cell:hover { border-color: var(--line-strong); filter: brightness(1.1); }
    .day-cell.active { border-color: #e7efe8; box-shadow: inset 0 0 0 1px #e7efe8; }
    .day-cell.today:after { content: ""; position: absolute; left: 5px; right: 5px; bottom: 2px; height: 2px; background: var(--blue); }
    .day-cell.none { background: var(--none); color: #747f8c; }
    .day-cell.low { background: var(--low); color: var(--ink); }
    .day-cell.mid { background: var(--mid); color: #07110a; }
    .day-cell.high { background: var(--high); color: #eef8ef; }
    .day-cell .dtop { display: flex; justify-content: space-between; gap: 4px; font-size: 10px; line-height: 1; }
    .day-cell .dbot { display: flex; justify-content: space-between; align-items: end; gap: 4px; font-size: 9.5px; line-height: 1; opacity: .92; }
    .axis-status { border-left: 1px solid var(--line); display: grid; grid-template-rows: 17px 1fr; min-width: 0; }
    .axis-status-title { padding: 0 8px; border-bottom: 1px solid var(--line-soft); color: var(--muted-2); display: flex; align-items: center; justify-content: space-between; gap: 8px; font-size: 9.5px; text-transform: uppercase; letter-spacing: .08em; }
    .axis-status-body { display: grid; grid-template-columns: repeat(4, 1fr); }
    .axis-kpi { padding: 5px 7px; border-right: 1px solid var(--line-soft); min-width: 0; }
    .axis-kpi:last-child { border-right: 0; }
    .axis-kpi span { display: block; color: var(--muted-2); font-size: 9.5px; text-transform: uppercase; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .axis-kpi b { display: block; margin-top: 1px; font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .app { height: calc(100vh - var(--axis-h)); display: grid; grid-template-columns: var(--left-w) minmax(390px, 1fr) var(--right-w); min-width: 0; }
    .panel { background: var(--panel); border-right: 1px solid var(--line); min-width: 0; overflow: hidden; display: grid; grid-template-rows: auto 1fr; }
    .panel.right { border-right: 0; border-left: 1px solid var(--line); }
    .panel-title { height: 32px; display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 0 9px; border-bottom: 1px solid var(--line); background: #0a0e13; font-size: 11px; text-transform: uppercase; letter-spacing: .07em; color: var(--muted); }
    .panel-title b { color: var(--text); font-size: 11px; }
    .panel-scroll { overflow: auto; min-height: 0; scrollbar-width: thin; scrollbar-color: #3a4652 transparent; }
    .summary-strip { padding: 8px 9px; border-bottom: 1px solid var(--line-soft); display: grid; gap: 5px; }
    .summary-strip h1 { margin: 0; font-size: 16px; line-height: 1.05; letter-spacing: -.01em; }
    .summary-strip p { margin: 0; color: var(--muted); font-size: 11px; line-height: 1.25; }
    .metrics-compact { display: grid; grid-template-columns: repeat(4, 1fr); border-bottom: 1px solid var(--line-soft); background: var(--panel-2); }
    .metric-c { padding: 6px 7px; border-right: 1px solid var(--line-soft); min-width: 0; }
    .metric-c:last-child { border-right: 0; }
    .metric-c span { display: block; color: var(--muted-2); font-size: 9.5px; line-height: 1; text-transform: uppercase; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .metric-c b { display: block; margin-top: 3px; font-size: 13px; line-height: 1; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .filter-bar { padding: 6px 7px; border-bottom: 1px solid var(--line-soft); display: flex; gap: 4px; overflow-x: auto; }
    .chip, .tiny-button { height: 24px; padding: 0 7px; border: 1px solid var(--line); background: var(--panel-2); color: var(--muted); border-radius: var(--radius); font-size: 11px; white-space: nowrap; }
    .chip:hover, .tiny-button:hover { border-color: var(--line-strong); color: var(--text); }
    .chip.active, .tiny-button.active { background: var(--high); border-color: var(--high); color: #f5fff6; }
    .event-list, .place-list { display: grid; gap: 1px; background: var(--line-soft); }
    .event-row { min-height: 48px; padding: 6px 7px; background: var(--panel); display: grid; grid-template-columns: 24px minmax(0, 1fr) auto; gap: 6px; align-items: center; text-align: left; border-left: 3px solid transparent; }
    .event-row:hover { background: var(--panel-2); }
    .event-row.active { background: #101b14; border-left-color: var(--low); }
    .event-icon { width: 22px; height: 22px; display: grid; place-items: center; border: 1px solid var(--line); background: var(--panel-2); border-radius: var(--radius); font-size: 12px; }
    .event-main { min-width: 0; display: grid; gap: 2px; }
    .event-title-line { display: flex; align-items: center; gap: 5px; min-width: 0; }
    .event-title-line b { font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .event-title-line span { color: var(--muted-2); font-size: 10px; white-space: nowrap; }
    .event-meta-line { color: var(--muted); font-size: 10.5px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .event-score { display: grid; gap: 2px; text-align: right; color: var(--muted); font-size: 10px; white-space: nowrap; }
    .pill { display: inline-flex; align-items: center; height: 17px; padding: 0 5px; border: 1px solid var(--line); border-radius: var(--radius); background: var(--panel-2); color: var(--muted); font-size: 9.5px; }
    .pill.high { background: var(--high); border-color: var(--high); color: #f4fff4; }
    .pill.low { background: var(--low); border-color: var(--low); color: var(--ink); }
    .empty { padding: 12px; color: var(--muted); font-size: 12px; background: var(--panel); }
    .workspace { min-width: 0; min-height: 0; display: grid; grid-template-rows: 34px 1fr 38px; background: #090d12; }
    .mapbar { display: grid; grid-template-columns: minmax(0,1fr) auto; align-items: center; gap: 8px; padding: 0 8px; border-bottom: 1px solid var(--line); background: #0a0e13; }
    .mapbar-title { min-width: 0; display: flex; align-items: baseline; gap: 8px; }
    .mapbar-title b { font-size: 12px; white-space: nowrap; }
    .mapbar-title span { color: var(--muted); font-size: 11px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .map-actions { display: flex; gap: 4px; }
    .map-wrap { position: relative; min-height: 0; }
    #map { width: 100%; height: 100%; background: #0b1117; }
    .map-empty { position: absolute; inset: 0; display: grid; place-items: center; padding: 16px; background: #0b1117; color: var(--muted); text-align: center; font-size: 12px; }
    .map-empty strong { color: var(--text); }
    .leaflet-container { background: #0b1117; font-family: inherit; }
    .leaflet-control-attribution { background: rgba(8,12,16,.88) !important; color: var(--muted) !important; }
    .micro-axis { display: grid; grid-template-columns: 74px minmax(0, 1fr) 168px; align-items: stretch; background: #080c10; border-top: 1px solid var(--line); min-width: 0; }
    .micro-label { display: grid; place-items: center; border-right: 1px solid var(--line); color: var(--muted-2); font-size: 9.5px; text-transform: uppercase; letter-spacing: .08em; }
    .micro-track { display: grid; grid-auto-flow: column; grid-auto-columns: minmax(24px, 1fr); gap: 1px; background: #151b22; overflow-x: auto; }
    .micro-day { position: relative; background: var(--none); border: 0; min-width: 24px; }
    .micro-day.low { background: var(--low); }
    .micro-day.mid { background: var(--mid); }
    .micro-day.high { background: var(--high); }
    .micro-day.active { box-shadow: inset 0 0 0 2px #e7efe8; }
    .micro-day.today:after { content:""; position:absolute; left:3px; right:3px; bottom:3px; height:2px; background:var(--blue); }
    .micro-summary { display: flex; align-items: center; justify-content: flex-end; gap: 7px; padding: 0 8px; color: var(--muted); font-size: 10.5px; border-left: 1px solid var(--line); white-space: nowrap; }
    .tabs { display: grid; grid-template-columns: repeat(3, 1fr); border-bottom: 1px solid var(--line-soft); background: var(--panel-2); }
    .tab { height: 28px; border-right: 1px solid var(--line-soft); color: var(--muted); font-size: 10.5px; text-transform: uppercase; letter-spacing: .06em; }
    .tab:last-child { border-right: 0; }
    .tab.active { background: var(--panel); color: var(--text); box-shadow: inset 0 -2px 0 var(--low); }
    .right-section { display: none; }
    .right-section.active { display: block; }
    .details-card { padding: 8px 9px; border-bottom: 1px solid var(--line-soft); }
    .details-card h2 { margin: 0 0 4px; font-size: 15px; line-height: 1.1; }
    .details-sub { color: var(--muted); font-size: 11px; line-height: 1.3; margin-bottom: 7px; }
    .detail-grid { display: grid; grid-template-columns: repeat(2, minmax(0,1fr)); border: 1px solid var(--line-soft); }
    .detail-item { padding: 6px 7px; border-right: 1px solid var(--line-soft); border-bottom: 1px solid var(--line-soft); min-width: 0; }
    .detail-item:nth-child(2n) { border-right: 0; }
    .detail-item span { display: block; color: var(--muted-2); font-size: 9.5px; text-transform: uppercase; line-height: 1; }
    .detail-item b { display: block; margin-top: 3px; font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .tag-row { display: flex; gap: 4px; flex-wrap: wrap; margin-top: 7px; }
    .density { display: grid; grid-template-columns: repeat(24, 1fr); gap: 1px; padding: 8px 9px 19px; border-bottom: 1px solid var(--line-soft); background: var(--panel); }
    .hour { height: 46px; background: var(--panel-2); display: grid; align-items: end; position: relative; }
    .hour-fill { width: 100%; min-height: 2px; background: var(--mid); }
    .hour span { position: absolute; bottom: -14px; left: 50%; transform: translateX(-50%); font-size: 8px; color: var(--muted-2); }
    .place-row { padding: 7px 9px; background: var(--panel); text-align: left; display: grid; gap: 3px; }
    .place-row:hover { background: var(--panel-2); }
    .place-row b { font-size: 12px; }
    .place-row span { color: var(--muted); font-size: 10.5px; }
    .table-compact { width: 100%; border-collapse: collapse; font-size: 11px; }
    .table-compact th, .table-compact td { border-bottom: 1px solid var(--line-soft); padding: 5px 7px; text-align: left; vertical-align: top; }
    .table-compact th { color: var(--muted-2); font-weight: 500; width: 42%; text-transform: uppercase; font-size: 9.5px; letter-spacing: .04em; }
    .iteration-note { padding: 7px 9px; color: var(--muted); font-size: 11px; line-height: 1.35; border-bottom: 1px solid var(--line-soft); background: var(--panel-2); }
    .iteration-note b { color: var(--text); }
    @media (max-width: 1180px) {
      body { overflow: auto; }
      .top-axis { grid-template-columns: 1fr; height: auto; position: sticky; }
      .brand-compact, .axis-status { display: none; }
      .axis-center { min-height: 56px; }
      .app { height: auto; min-height: calc(100vh - 56px); grid-template-columns: 1fr; }
      .panel, .panel.right { border-left: 0; border-right: 0; }
      .workspace { min-height: 72vh; }
    }
  </style>
</head>
<body>
  <script id="coord-report-data" type="application/json">__REPORT_JSON__</script>
  <header class="top-axis">
    <div class="brand-compact"><b>Coord Intel</b><span id="generatedLabel">single file report</span></div>
    <div class="axis-center">
      <div class="axis-labels"><span>calendar density</span><span id="axisRangeLabel">-</span></div>
      <div class="day-rail" id="topDayAxis"></div>
    </div>
    <div class="axis-status">
      <div class="axis-status-title"><span id="axisLevelLabel">activity</span><span id="dayCompactLabel">-</span></div>
      <div class="axis-status-body">
        <div class="axis-kpi"><span>Pts</span><b id="axisPoints">0</b></div>
        <div class="axis-kpi"><span>Km</span><b id="axisKm">0</b></div>
        <div class="axis-kpi"><span>Events</span><b id="axisEvents">0</b></div>
        <div class="axis-kpi"><span>GPS</span><b id="axisGps">-</b></div>
      </div>
    </div>
  </header>
  <main class="app">
    <aside class="panel">
      <div class="panel-title"><b>Day intelligence</b><span id="eventCountLabel">0</span></div>
      <div class="panel-scroll">
        <section class="summary-strip"><h1 id="dayTitle">-</h1><p id="dayNarrative">Loading report...</p></section>
        <section class="metrics-compact">
          <div class="metric-c"><span>Points</span><b id="mPoints">0</b></div>
          <div class="metric-c"><span>Distance</span><b id="mDistance">0</b></div>
          <div class="metric-c"><span>Events</span><b id="mEvents">0</b></div>
          <div class="metric-c"><span>Accuracy</span><b id="mQuality">-</b></div>
        </section>
        <nav class="filter-bar">
          <button class="chip active" data-filter="all">All</button>
          <button class="chip" data-filter="stop">Stops</button>
          <button class="chip" data-filter="movement">Moves</button>
          <button class="chip" data-filter="interesting">Interesting</button>
          <button class="chip" data-filter="quality">Quality</button>
        </nav>
        <section class="event-list" id="eventList"></section>
      </div>
    </aside>
    <section class="workspace">
      <div class="mapbar">
        <div class="mapbar-title"><b>Spatial view</b><span id="mapSubtitle">-</span></div>
        <div class="map-actions">
          <button class="tiny-button active" id="toggleEvents">Events</button>
          <button class="tiny-button" id="toggleRaw">Raw</button>
          <button class="tiny-button" id="fitMap">Fit</button>
        </div>
      </div>
      <div class="map-wrap"><div id="map"></div><div class="map-empty" id="emptyMapState" hidden><div><strong>No activity</strong><br>Select an active day from the axis.</div></div></div>
      <footer class="micro-axis"><div class="micro-label">mini</div><div class="micro-track" id="bottomDayAxis"></div><div class="micro-summary" id="bottomSummary">-</div></footer>
    </section>
    <aside class="panel right">
      <div class="panel-title"><b>Inspector</b><span id="detailsKind">-</span></div>
      <nav class="tabs"><button class="tab active" data-tab="details">Event</button><button class="tab" data-tab="places">Places</button><button class="tab" data-tab="diag">Diag</button></nav>
      <div class="panel-scroll">
        <section class="right-section active" id="tab-details"><div class="details-card" id="detailsPanel"></div><div class="panel-title"><b>Hours</b><span id="hourLabel">points</span></div><div class="density" id="hourDensity"></div></section>
        <section class="right-section" id="tab-places"><div class="iteration-note"><b>Places:</b> local stop clusters inferred from GPS only. No geocoding is used.</div><div class="place-list" id="placeList"></div></section>
        <section class="right-section" id="tab-diag"><div class="iteration-note"><b>Diagnostics:</b> static single-page report. Map is optional; event list works without Leaflet.</div><table class="table-compact"><tbody id="diagnosticsRows"></tbody></table></section>
      </div>
    </aside>
  </main>
  <script>
    const report = JSON.parse(document.getElementById('coord-report-data').textContent);
    const points = report.points || [];
    const events = report.events || [];
    const places = report.places || [];
    const dayMap = new Map((report.days || []).map(d => [d.date, d]));
    let selectedDay = (report.days && report.days[0] && report.days[0].date) || toDateKey(new Date());
    let selectedEventId = null;
    let activeFilter = 'all';
    let showRaw = false;
    let showEvents = true;
    let map = null;
    let routeLayer = null;
    let eventLayer = null;
    let rawLayer = null;
    let lastBounds = null;
    const $ = id => document.getElementById(id);
    function toDateKey(value) { const d = value instanceof Date ? value : new Date(value); if (Number.isNaN(d.getTime())) return ''; return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`; }
    function startOfDay(dateKey) { return new Date(`${dateKey}T00:00:00`); }
    function addDays(date, amount) { const d = new Date(date.getTime()); d.setDate(d.getDate() + amount); return d; }
    function shortDate(dateKey) { return new Date(`${dateKey}T12:00:00`).toLocaleDateString(undefined, { day: '2-digit', month: '2-digit' }); }
    function dayName(dateKey) { return new Date(`${dateKey}T12:00:00`).toLocaleDateString(undefined, { weekday: 'short' }); }
    function fullDate(dateKey) { return new Date(`${dateKey}T12:00:00`).toLocaleDateString(undefined, { weekday: 'short', day: '2-digit', month: 'long', year: 'numeric' }); }
    function fmtTime(v) { const d = new Date(v); return Number.isNaN(d.getTime()) ? '-' : d.toLocaleTimeString(undefined, {hour:'2-digit', minute:'2-digit'}); }
    function fmtDur(sec) { sec=Math.max(0,Math.round(Number(sec)||0)); if(sec<60)return `${sec}s`; const m=Math.floor(sec/60); if(m<60)return `${m}m`; const h=Math.floor(m/60); return `${h}h ${m%60}m`; }
    function fmtDist(m) { m=Number(m)||0; return m>=1000 ? `${(m/1000).toFixed(m>=10000?1:2)} km` : `${Math.round(m)} m`; }
    function esc(v) { return String(v ?? '').replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch])); }
    function eventDay(e) { return toDateKey(e.start_at || e.end_at); }
    function axisDays() { const dayKeys = Array.from(dayMap.keys()).sort(); const base = selectedDay || dayKeys[dayKeys.length - 1] || toDateKey(new Date()); const start = addDays(startOfDay(base), -8); const out = []; for (let i=0;i<17;i++) out.push(toDateKey(addDays(start,i))); return out; }
    function activityLevel(day) { if (!day || !day.points) return 'none'; if (day.points < 30 && day.distance_m < 750) return 'low'; if (day.points < 180 && day.distance_m < 5000) return 'mid'; return 'high'; }
    function activityText(day) { const lvl = activityLevel(day); return lvl === 'none' ? 'none' : lvl === 'low' ? 'low' : lvl === 'mid' ? 'medium' : 'high'; }
    function dayPts(dateKey) { return points.filter(p => toDateKey(p.received_at || p.recorded_at) === dateKey); }
    function dayEvents(dateKey) { return events.filter(e => eventDay(e) === dateKey); }
    function filteredEvents(list) { if (activeFilter === 'all') return list; if (activeFilter === 'quality') return list.filter(e => ['low_accuracy','gps_gap','quality'].includes(e.type) || (e.tags||[]).includes('quality')); if (activeFilter === 'interesting') return list.filter(e => (e.tags || []).includes('interesting') || e.severity === 'important'); return list.filter(e => e.type === activeFilter); }
    function summarize(dateKey) { const day = dayMap.get(dateKey); const ev = dayEvents(dateKey); const pts = dayPts(dateKey); if (day) return {...day, events: ev, points_list: pts}; return {date: dateKey, points: 0, distance_m: 0, duration_s: 0, avg_accuracy_m: null, event_count: 0, events: [], points_list: []}; }
    function icon(type) { if (type === 'stop') return 'S'; if (type === 'movement') return 'M'; if (type === 'low_accuracy') return '!'; if (type === 'gps_gap') return '..'; if (type === 'place') return 'P'; return '*'; }
    function renderAxes() { const days = axisDays(); const today = toDateKey(new Date()); const makeTop = dateKey => { const day = dayMap.get(dateKey); const lvl = activityLevel(day); return `<button class="day-cell ${lvl} ${dateKey===selectedDay?'active':''} ${dateKey===today?'today':''}" data-day="${dateKey}" title="${dateKey} ${activityText(day)}"><span class="dtop"><b>${dayName(dateKey)}</b><span>${shortDate(dateKey)}</span></span><span class="dbot"><span>${day ? day.points : 0}p</span><span>${day ? (day.event_count || 0) : 0}e</span></span></button>`; }; const makeBottom = dateKey => { const day = dayMap.get(dateKey); const lvl = activityLevel(day); return `<button class="micro-day ${lvl} ${dateKey===selectedDay?'active':''} ${dateKey===today?'today':''}" data-day="${dateKey}" title="${dateKey} ${activityText(day)}"></button>`; }; $('topDayAxis').innerHTML = days.map(makeTop).join(''); $('bottomDayAxis').innerHTML = days.map(makeBottom).join(''); document.querySelectorAll('[data-day]').forEach(btn => btn.addEventListener('click', () => selectDay(btn.dataset.day))); $('axisRangeLabel').textContent = `${days[0]} -> ${days[days.length - 1]}`; }
    function renderSummary() { const s = summarize(selectedDay); const level = activityText(dayMap.get(selectedDay)); $('generatedLabel').textContent = `${report.meta?.range_label || 'range'} generated ${new Date(report.meta?.generated_at || Date.now()).toLocaleString()}`; $('dayTitle').textContent = fullDate(selectedDay); $('dayCompactLabel').textContent = `${selectedDay} ${level}`; $('dayNarrative').textContent = s.points ? `This day has ${s.points} points, ${s.event_count || s.events.length} events, ${fmtDist(s.distance_m)} and average accuracy ${s.avg_accuracy_m == null ? '-' : Math.round(s.avg_accuracy_m) + ' m'}.` : 'No GPS activity for this day.'; $('mPoints').textContent = s.points; $('mDistance').textContent = fmtDist(s.distance_m); $('mEvents').textContent = s.event_count || s.events.length; $('mQuality').textContent = s.avg_accuracy_m == null ? '-' : `${Math.round(s.avg_accuracy_m)} m`; $('axisPoints').textContent = s.points; $('axisKm').textContent = s.distance_m ? (s.distance_m/1000).toFixed(1) : '0'; $('axisEvents').textContent = s.event_count || s.events.length; $('axisGps').textContent = s.avg_accuracy_m == null ? '-' : `${Math.round(s.avg_accuracy_m)}m`; $('axisLevelLabel').textContent = level; $('bottomSummary').textContent = `${selectedDay} - ${s.points} pts - ${fmtDist(s.distance_m)} - ${s.event_count || s.events.length} ev`; $('mapSubtitle').textContent = s.points ? `${fmtDist(s.distance_m)} - ${s.points} points - ${s.event_count || s.events.length} events` : 'no points for selected day'; }
    function renderEvents() { const list = filteredEvents(dayEvents(selectedDay)); const host = $('eventList'); $('eventCountLabel').textContent = `${list.length}`; if (!list.length) { host.innerHTML = '<div class="empty">No events for the selected filter.</div>'; selectedEventId = null; renderDetails(null); return; } if (!selectedEventId || !list.some(e => e.id === selectedEventId)) selectedEventId = list[0].id; host.innerHTML = list.map(e => { const tags = e.tags || []; const important = e.severity === 'important' || tags.includes('interesting'); const meta = [fmtDur(e.duration_s), fmtDist(e.distance_m), `${e.point_count || 0} pts`, e.avg_accuracy_m != null ? `${Math.round(e.avg_accuracy_m)}m GPS` : null].filter(Boolean).join(' - '); return `<button class="event-row ${e.id===selectedEventId?'active':''}" data-event="${esc(e.id)}"><span class="event-icon">${icon(e.type)}</span><span class="event-main"><span class="event-title-line"><b>${esc(e.title || e.type)}</b><span>${fmtTime(e.start_at)}-${fmtTime(e.end_at)}</span></span><span class="event-meta-line">${esc(meta)}</span></span><span class="event-score"><span class="pill ${important?'high':''}">${important ? 'hot' : e.type}</span><span>${Math.round((e.confidence || 0) * 100)}%</span></span></button>`; }).join(''); host.querySelectorAll('[data-event]').forEach(btn => btn.addEventListener('click', () => selectEvent(btn.dataset.event))); renderDetails(events.find(e => e.id === selectedEventId)); }
    function renderDetails(e) { const host = $('detailsPanel'); if (!e) { $('detailsKind').textContent = 'none'; host.innerHTML = '<h2>No event</h2><div class="details-sub">Select an active day or change the filter.</div>'; return; } $('detailsKind').textContent = e.type; const mapUrl = e.center ? `https://maps.google.com/?q=${e.center.lat.toFixed(7)},${e.center.lon.toFixed(7)}` : '#'; host.innerHTML = `<h2>${esc(e.title || e.type)}</h2><div class="details-sub">${esc(e.subtitle || '')}</div><div class="detail-grid"><div class="detail-item"><span>Time</span><b>${fmtTime(e.start_at)}-${fmtTime(e.end_at)}</b></div><div class="detail-item"><span>Duration</span><b>${fmtDur(e.duration_s)}</b></div><div class="detail-item"><span>Distance</span><b>${fmtDist(e.distance_m)}</b></div><div class="detail-item"><span>Points</span><b>${e.point_count || 0}</b></div><div class="detail-item"><span>Accuracy</span><b>${e.avg_accuracy_m == null ? '-' : Math.round(e.avg_accuracy_m) + ' m'}</b></div><div class="detail-item"><span>Confidence</span><b>${Math.round((e.confidence || 0) * 100)}%</b></div><div class="detail-item"><span>Center</span><b>${e.center ? e.center.lat.toFixed(5)+', '+e.center.lon.toFixed(5) : '-'}</b></div><div class="detail-item"><span>Map</span><b><a href="${mapUrl}" target="_blank" rel="noreferrer">Google Maps</a></b></div></div><div class="tag-row">${(e.tags || []).slice(0,8).map(t => `<span class="pill">${esc(t)}</span>`).join('')}</div>`; }
    function renderDensity() { const counts = Array.from({length:24}, () => 0); dayPts(selectedDay).forEach(p => { const d = new Date(p.received_at || p.recorded_at); if (!Number.isNaN(d.getTime())) counts[d.getHours()] += 1; }); const max = Math.max(1, ...counts); $('hourDensity').innerHTML = counts.map((c,h) => `<div class="hour" title="${h}:00 ${c} pts"><div class="hour-fill" style="height:${Math.max(3, Math.round(c/max*44))}px"></div>${h%4===0?`<span>${h}</span>`:''}</div>`).join(''); }
    function renderPlaces() { const dayEvIds = new Set(dayEvents(selectedDay).map(e => e.id)); const list = places.filter(p => (p.event_ids || []).some(id => dayEvIds.has(id))); const host = $('placeList'); if (!list.length) { host.innerHTML = '<div class="empty">No places for this day.</div>'; return; } host.innerHTML = list.map(p => `<button class="place-row" data-place="${esc(p.id)}"><b>${esc(p.label || p.name || p.id)}</b><span>${p.visit_count || 0} visits - ${fmtDur(p.total_duration_s)} - ${p.point_count || 0} pts</span></button>`).join(''); host.querySelectorAll('[data-place]').forEach(btn => btn.addEventListener('click', () => { const p = places.find(x => x.id === btn.dataset.place); const first = p && (p.event_ids || []).find(id => dayEvIds.has(id)); if (first) { activateTab('details'); selectEvent(first); } })); }
    function renderDiagnostics() { const s = summarize(selectedDay); const rows = [['Date', selectedDay], ['Activity', activityText(dayMap.get(selectedDay))], ['Points', s.points], ['Events', s.event_count || s.events.length], ['Distance', fmtDist(s.distance_m)], ['Duration', fmtDur(s.duration_s)], ['Avg accuracy', s.avg_accuracy_m == null ? '-' : Math.round(s.avg_accuracy_m) + ' m'], ['Providers', JSON.stringify(report.summary?.providers || {})], ['Privacy', report.settings?.privacy_note || 'Static local report']]; $('diagnosticsRows').innerHTML = rows.map(([k,v]) => `<tr><th>${esc(k)}</th><td>${esc(v)}</td></tr>`).join(''); }
    function showMapUnavailable(message) { $('emptyMapState').hidden = false; $('emptyMapState').innerHTML = `<div><strong>Map unavailable</strong><br>${message}<br>Calendar, events and inspector still work.</div>`; }
    function initMap() { if (!window.L) { showMapUnavailable('Leaflet did not load.'); return; } try { map = L.map('map', { preferCanvas: true, zoomControl: true }); L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '&copy; OpenStreetMap' }).addTo(map); eventLayer = L.layerGroup().addTo(map); rawLayer = L.layerGroup().addTo(map); } catch (e) { showMapUnavailable(String(e.message || e)); } }
    function loadMapLibrary() { if (window.L) { initMap(); renderMap(); return; } const script = document.createElement('script'); script.src = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js'; script.async = true; const timer = window.setTimeout(() => showMapUnavailable('Map loading timed out or there is no internet.'), 2500); script.onload = () => { window.clearTimeout(timer); initMap(); renderMap(); }; script.onerror = () => { window.clearTimeout(timer); showMapUnavailable('Could not download Leaflet.'); }; document.head.appendChild(script); }
    function renderMap() { const pts = dayPts(selectedDay).filter(p => Number.isFinite(p.lat) && Number.isFinite(p.lon)); const evs = dayEvents(selectedDay); $('emptyMapState').hidden = !!pts.length; if (!map || !window.L) return; try { if (routeLayer) map.removeLayer(routeLayer); eventLayer.clearLayers(); rawLayer.clearLayers(); lastBounds = null; if (!pts.length) { map.setView([52.0, 19.0], 6); return; } const latlngs = pts.map(p => [p.lat, p.lon]); routeLayer = L.polyline(latlngs, { color: '#4d8d57', weight: 3, opacity: .92 }).addTo(map); lastBounds = L.latLngBounds(latlngs); if (showEvents) { evs.forEach(e => { if (!e.center) return; const selected = e.id === selectedEventId; const color = e.type === 'movement' ? '#7698cf' : e.type === 'stop' ? '#a7c8a6' : '#b4974a'; const marker = L.circleMarker([e.center.lat, e.center.lon], { radius: selected ? 8 : 6, color, fillColor: color, fillOpacity: selected ? 1 : .85, weight: selected ? 3 : 2 }); marker.bindPopup(`<b>${esc(e.title || e.type)}</b><br>${fmtTime(e.start_at)}-${fmtTime(e.end_at)}<br>${fmtDur(e.duration_s)} - ${fmtDist(e.distance_m)}`); marker.on('click', () => selectEvent(e.id)); marker.addTo(eventLayer); }); } if (showRaw) { pts.forEach((p,i) => { L.circleMarker([p.lat, p.lon], { radius: i===0 || i===pts.length-1 ? 4 : 2, color: '#687380', fillColor: '#687380', fillOpacity: .65, weight: 1 }).addTo(rawLayer); }); } requestAnimationFrame(() => { map.invalidateSize(); if (lastBounds && lastBounds.isValid()) map.fitBounds(lastBounds, { padding: [18,18], maxZoom: 17 }); }); } catch (e) { showMapUnavailable(String(e.message || e)); } }
    function focusEvent(e) { if (!e || !e.center || !map || !window.L) return; try { map.setView([e.center.lat, e.center.lon], Math.max(map.getZoom() || 15, 16), { animate: true }); } catch (_) {} }
    function selectEvent(id) { selectedEventId = id; const e = events.find(x => x.id === id); renderEvents(); renderDetails(e); renderMap(); focusEvent(e); }
    function selectDay(dateKey) { selectedDay = dateKey; const evs = dayEvents(dateKey); selectedEventId = evs[0]?.id || null; renderAll(); }
    function activateTab(tab) { document.querySelectorAll('.tab').forEach(b => b.classList.toggle('active', b.dataset.tab === tab)); document.querySelectorAll('.right-section').forEach(s => s.classList.toggle('active', s.id === `tab-${tab}`)); }
    function renderAll() { renderAxes(); renderSummary(); renderEvents(); renderDensity(); renderPlaces(); renderDiagnostics(); renderMap(); }
    document.querySelectorAll('[data-filter]').forEach(btn => btn.addEventListener('click', () => { activeFilter = btn.dataset.filter; document.querySelectorAll('[data-filter]').forEach(b => b.classList.toggle('active', b.dataset.filter === activeFilter)); selectedEventId = null; renderEvents(); renderMap(); }));
    document.querySelectorAll('[data-tab]').forEach(btn => btn.addEventListener('click', () => activateTab(btn.dataset.tab)));
    $('toggleEvents').addEventListener('click', () => { showEvents = !showEvents; $('toggleEvents').classList.toggle('active', showEvents); renderMap(); });
    $('toggleRaw').addEventListener('click', () => { showRaw = !showRaw; $('toggleRaw').classList.toggle('active', showRaw); renderMap(); });
    $('fitMap').addEventListener('click', () => { if (map && lastBounds && lastBounds.isValid()) map.fitBounds(lastBounds, { padding: [18,18], maxZoom: 17 }); });
    renderAll();
    loadMapLibrary();
  </script>
</body>
</html>"""
    return template.replace('__REPORT_JSON__', report_json)
def build_calendar_block(records: List[Dict[str, object]]) -> str:
    if not records:
        return "<section class=\"point\"><strong>Calendar</strong><div class=\"meta\">No location events in this range.</div></section>"
    grouped: Dict[str, List[Dict[str, object]]] = {}
    for record in records:
        label = local_time(record.get("received_at"))[:13] + ":00"
        grouped.setdefault(label, []).append(record)
    rows = []
    for label, items in grouped.items():
        first = items[0]
        last = items[-1]
        distance = 0.0
        for prev, curr in zip(items, items[1:]):
            distance += haversine_m(float(prev["lat"]), float(prev["lon"]), float(curr["lat"]), float(curr["lon"]))
        rows.append(
            "<div class=\"point\">"
            f"<span class=\"time\">{escape(label)}</span>"
            f"<div class=\"coords\">{len(items)} points | {distance / 1000.0:.2f} km</div>"
            f"<div class=\"meta\">{float(first['lat']):.5f},{float(first['lon']):.5f} -> {float(last['lat']):.5f},{float(last['lon']):.5f}</div>"
            "</div>"
        )
    return "<section><h2 style=\"font-size:18px;margin:18px 0 8px\">Calendar</h2>" + "".join(rows[:120]) + "</section>"
def build_ai_summary_block(llm_summary: Dict[str, object]) -> str:
    title = escape(str(llm_summary.get("title") or "Timeline summary"))
    summary = escape(str(llm_summary.get("summary") or "").strip())
    movement = escape(str(llm_summary.get("movement_pattern") or "").strip())
    quality = escape(str(llm_summary.get("data_quality") or "").strip())
    notable = llm_summary.get("notable_points") or []
    if not isinstance(notable, list):
        notable = []
    items = "".join(f"<li>{escape(str(item))}</li>" for item in notable[:6] if str(item).strip())
    return (
        "<section class=\"point\">"
        f"<strong>{title}</strong>"
        f"{f'<div class=\"meta\">{summary}</div>' if summary else ''}"
        f"{f'<div class=\"meta\">Movement: {movement}</div>' if movement else ''}"
        f"{f'<ul class=\"meta\">{items}</ul>' if items else ''}"
        f"{f'<div class=\"meta\">Data quality: {quality}</div>' if quality else ''}"
        "</section>"
    )
def build_point_card(point: Dict[str, object]) -> str:
    battery = point.get("battery_percent")
    accuracy = point.get("accuracy_m")
    speed = point.get("speed_mps")
    charging = " charging" if point.get("charging") else ""
    meta = []
    if accuracy is not None:
        meta.append(f"Accuracy {float(accuracy):.0f} m")
    if speed is not None:
        meta.append(f"Speed {float(speed):.1f} m/s")
    if battery is not None:
        meta.append(f"Battery {int(battery)}%{charging}")
    return (
        f"<div class=\"point\">"
        f"<span class=\"time\">{escape(local_time(point.get('received_at')))}</span>"
        f"<div class=\"coords\"><code>{float(point['lat']):.7f}, {float(point['lon']):.7f}</code></div>"
        f"<div class=\"meta\">{' | '.join(meta) if meta else 'No extra metadata'}</div>"
        f"</div>"
    )
def build_timeline_export(range_value: str) -> Dict[str, object]:
    requested = str(range_value or "").strip().lower()
    if requested in {"all", "history", "full"}:
        since_iso = ""
        records = history_all()
        scope_label = "all"
    else:
        seconds = parse_duration_to_seconds(range_value)
        cutoff = datetime.now(timezone.utc) - timedelta(seconds=seconds)
        since_iso = cutoff.isoformat(timespec="seconds")
        records = history_since(since_iso)
        scope_label = canonical_range_label(range_value)
    all_records = history_all()
    summary = summarize_locations(records)
    llm = timeline_llm_summary(records, summary, scope_label)
    html = build_timeline_html(records, scope_label, llm, all_records=all_records)
    safe_scope = scope_label.replace(" ", "_").replace("/", "-")
    export_name = f"coord-timeline-{safe_scope}-{datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')}.html"
    export_path = EXPORTS_DIR / export_name
    export_path.write_text(html, encoding="utf-8")
    return {
        "path": export_path,
        "records": records,
        "summary": summary,
        "llm_summary": llm,
        "scope_label": scope_label,
        "since_iso": since_iso,
    }
