using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Surveillance.Capabilities;

internal static class SurveillanceCapabilities
{
    private static CapabilityMetadata Meta(string name, string command, string description, string permission = "ALL") =>
        new(name, command, description, permission, RiskLevel.Safe, IsReadOnly: true, Scope: "surveillance");

    public static readonly IReadOnlyList<CapabilityMetadata> All =
    [
        Meta("surveillance.panel", "/panel", "Open the surveillance control panel."),
        Meta("surveillance.types", "/types", "Show supported surveillance event types and filters."),
        Meta("surveillance.stats", "/stats", "Show compact surveillance stats for a time window."),
        Meta("surveillance.digest", "/digest", "Show a compact operational digest."),
        Meta("surveillance.priority", "/priority", "Show highest-priority incidents."),
        Meta("surveillance.llm_status", "/llm_status", "Show local LLM enrichment status for surveillance."),
        Meta("surveillance.storage", "/storage", "Show surveillance storage paths and usage."),
        Meta("surveillance.latest_snapshot", "/latest", "Send the latest still image."),
        Meta("surveillance.live_snapshot", "/live", "Capture and send a fresh still image."),
        Meta("surveillance.snapshot", "/snapshot", "Send the latest prototype clip."),
        Meta("surveillance.events", "/events", "List recent events."),
        Meta("surveillance.incidents", "/incidents", "List recent incidents."),
        Meta("surveillance.incident", "/incident", "Show one incident in detail."),
        Meta("surveillance.timeline", "/timeline", "Show incident timeline for a selected range."),
        Meta("surveillance.event", "/event", "Show one event in detail."),
        Meta("surveillance.event_snapshot", "/event_snapshot", "Send one event snapshot."),
        Meta("surveillance.event_transcript", "/event_transcript", "Send one event transcript."),
        Meta("surveillance.event_clip", "/event_clip", "Download one event clip with audio."),
        Meta("surveillance.event_preview", "/event_preview", "Send a GIF preview of one event."),
        Meta("surveillance.incident_snapshot", "/incident_snapshot", "Send one incident snapshot."),
        Meta("surveillance.incident_transcript", "/incident_transcript", "Send one incident transcript."),
        Meta("surveillance.incident_clip", "/incident_clip", "Download one incident clip with audio."),
        Meta("surveillance.summary", "/summary", "Build a full AI summary for a time range or event.")
    ];
}