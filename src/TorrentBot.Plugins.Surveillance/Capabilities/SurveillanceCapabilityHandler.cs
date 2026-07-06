using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Plugins.Surveillance.Capabilities;

public sealed class SurveillanceCapabilityHandler(string capabilityName) : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var client = context.Engine.GetService<ISurveillanceClient>();
        if (client is null)
        {
            return new CapabilityResult(Success: false, Message: "Surveillance client is not available.", IsDryRun: context.IsDryRun);
        }

        var hours = GetString(parameters, "hours");
        var type = GetString(parameters, "type");
        var eventId = GetString(parameters, "event_id") ?? GetString(parameters, "id");
        var incidentId = GetString(parameters, "incident_id") ?? GetString(parameters, "id");
        var target = GetString(parameters, "target");

        Dictionary<string, object?> data;
        string message;

        switch (capabilityName)
        {
            case "surveillance.panel":
                data = await client.GetPanelAsync(cancellationToken).ConfigureAwait(false);
                message = "Surveillance panel loaded.";
                break;
            case "surveillance.types":
                data = new Dictionary<string, object?> { ["types"] = new[] { "motion", "audio", "person", "vehicle" } };
                message = "Surveillance event types listed.";
                break;
            case "surveillance.stats":
                var stats = await client.GetStatsAsync(hours, cancellationToken).ConfigureAwait(false);
                data = new Dictionary<string, object?>
                {
                    ["window"] = stats.Window,
                    ["event_count"] = stats.EventCount,
                    ["incident_count"] = stats.IncidentCount,
                    ["top_types"] = stats.TopTypes
                };
                message = $"Surveillance stats for {stats.Window}.";
                break;
            case "surveillance.digest":
            case "surveillance.priority":
            case "surveillance.timeline":
                var incidents = await client.GetIncidentsAsync(type, hours, cancellationToken).ConfigureAwait(false);
                data = new Dictionary<string, object?>
                {
                    ["hours"] = hours ?? "24h",
                    ["type"] = type,
                    ["incidents"] = incidents.Select(i => new Dictionary<string, object?>
                    {
                        ["id"] = i.Id,
                        ["type"] = i.Type,
                        ["summary"] = i.Summary,
                        ["priority"] = i.Priority
                    }).ToList()
                };
                message = $"{capabilityName} returned {incidents.Count} incident(s).";
                break;
            case "surveillance.llm_status":
                data = await client.GetLlmStatusAsync(cancellationToken).ConfigureAwait(false);
                message = "Surveillance LLM status loaded.";
                break;
            case "surveillance.storage":
                data = await client.GetStorageAsync(cancellationToken).ConfigureAwait(false);
                message = "Surveillance storage status loaded.";
                break;
            case "surveillance.events":
                var events = await client.GetEventsAsync(type, hours, cancellationToken).ConfigureAwait(false);
                data = new Dictionary<string, object?>
                {
                    ["hours"] = hours ?? "24h",
                    ["type"] = type,
                    ["events"] = events.Select(e => new Dictionary<string, object?>
                    {
                        ["id"] = e.Id,
                        ["type"] = e.Type,
                        ["summary"] = e.Summary,
                        ["severity"] = e.Severity,
                        ["occurred_at"] = e.OccurredAt
                    }).ToList(),
                    ["count"] = events.Count
                };
                message = $"Listed {events.Count} surveillance event(s).";
                break;
            case "surveillance.incidents":
                var incidentList = await client.GetIncidentsAsync(type, hours, cancellationToken).ConfigureAwait(false);
                data = new Dictionary<string, object?>
                {
                    ["incidents"] = incidentList.Select(i => new Dictionary<string, object?> { ["id"] = i.Id, ["summary"] = i.Summary }).ToList(),
                    ["count"] = incidentList.Count
                };
                message = $"Listed {incidentList.Count} incident(s).";
                break;
            case "surveillance.event":
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    return new CapabilityResult(Success: false, Message: "Parameter 'event_id' is required.", IsDryRun: context.IsDryRun);
                }

                data = await client.GetEventAsync(eventId, cancellationToken).ConfigureAwait(false);
                message = $"Loaded event '{eventId}'.";
                break;
            case "surveillance.incident":
                if (string.IsNullOrWhiteSpace(incidentId))
                {
                    return new CapabilityResult(Success: false, Message: "Parameter 'incident_id' is required.", IsDryRun: context.IsDryRun);
                }

                data = await client.GetIncidentAsync(incidentId, cancellationToken).ConfigureAwait(false);
                message = $"Loaded incident '{incidentId}'.";
                break;
            case "surveillance.summary":
                data = new Dictionary<string, object?>
                {
                    ["target"] = target ?? hours ?? "24h",
                    ["summary"] = "Automated surveillance summary for the requested window."
                };
                message = "Surveillance summary generated.";
                break;
            default:
                var mediaKind = capabilityName switch
                {
                    "surveillance.latest_snapshot" or "surveillance.live_snapshot" or "surveillance.event_snapshot" or "surveillance.incident_snapshot" => "snapshot",
                    "surveillance.event_clip" or "surveillance.incident_clip" or "surveillance.snapshot" => "clip",
                    "surveillance.event_preview" => "preview",
                    _ => "media"
                };
                var media = await client.GetMediaAsync(mediaKind, eventId, incidentId, cancellationToken).ConfigureAwait(false);
                data = new Dictionary<string, object?>
                {
                    ["capability"] = capabilityName,
                    ["event_id"] = eventId,
                    ["incident_id"] = incidentId,
                    ["media"] = media.MediaKind,
                    ["content_type"] = media.ContentType,
                    ["file_name"] = media.FileName,
                    ["size"] = media.Content.Length,
                    ["deliverable"] = true,
                    ["base64"] = Convert.ToBase64String(media.Content)
                };
                message = $"{capabilityName} media payload ready ({media.FileName}).";
                break;
        }

        return new CapabilityResult(Success: true, Data: data, Message: message, IsDryRun: context.IsDryRun);
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}