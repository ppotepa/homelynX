using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Integrations.Fakes;

public sealed class FakeSurveillanceClient : ISurveillanceClient
{
    private readonly List<SurveillanceEventItem> _events =
    [
        new("evt-1001", "motion", "Motion at front door", DateTimeOffset.UtcNow.AddHours(-2), "info"),
        new("evt-1002", "audio", "Glass break detected", DateTimeOffset.UtcNow.AddHours(-1), "critical"),
        new("evt-1003", "person", "Person in driveway", DateTimeOffset.UtcNow.AddMinutes(-20), "warning")
    ];

    public Task<SurveillanceStats> GetStatsAsync(string? hours = null, CancellationToken ct = default) =>
        Task.FromResult(new SurveillanceStats(_events.Count, 1, hours ?? "24h", ["motion", "audio", "person"]));

    public Task<IReadOnlyList<SurveillanceEventItem>> GetEventsAsync(string? type = null, string? hours = null, CancellationToken ct = default)
    {
        var items = string.IsNullOrWhiteSpace(type)
            ? _events
            : _events.Where(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase)).ToList();
        return Task.FromResult<IReadOnlyList<SurveillanceEventItem>>(items);
    }

    public Task<IReadOnlyList<SurveillanceIncidentItem>> GetIncidentsAsync(string? type = null, string? hours = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SurveillanceIncidentItem>>(
        [
            new("inc-501", type ?? "audio", "Glass break cluster", DateTimeOffset.UtcNow.AddHours(-1), "critical")
        ]);

    public Task<Dictionary<string, object?>> GetPanelAsync(CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, object?>
        {
            ["status"] = "online",
            ["cameras"] = 4,
            ["recorder"] = "surveillance-api",
            ["events_today"] = _events.Count
        });

    public Task<Dictionary<string, object?>> GetEventAsync(string eventId, CancellationToken ct = default)
    {
        var evt = _events.FirstOrDefault(e => e.Id == eventId);
        return Task.FromResult(evt is null
            ? new Dictionary<string, object?> { ["found"] = false, ["event_id"] = eventId }
            : new Dictionary<string, object?>
            {
                ["found"] = true,
                ["id"] = evt.Id,
                ["type"] = evt.Type,
                ["summary"] = evt.Summary,
                ["severity"] = evt.Severity,
                ["occurred_at"] = evt.OccurredAt
            });
    }

    public Task<Dictionary<string, object?>> GetIncidentAsync(string incidentId, CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, object?>
        {
            ["found"] = true,
            ["id"] = incidentId,
            ["priority"] = "critical",
            ["events"] = _events.Select(e => e.Id).ToArray()
        });

    public Task<Dictionary<string, object?>> GetStorageAsync(CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, object?>
        {
            ["clips_path"] = "/surveillance-data/clips",
            ["used_gb"] = 128.5,
            ["free_gb"] = 900.0
        });

    public Task<Dictionary<string, object?>> GetLlmStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(new Dictionary<string, object?>
        {
            ["enabled"] = true,
            ["model"] = "llama3",
            ["queue_depth"] = 0
        });

    public Task<SurveillanceMediaPayload> GetMediaAsync(string mediaKind, string? eventId = null, string? incidentId = null, CancellationToken ct = default)
    {
        var id = eventId ?? incidentId ?? "latest";
        var bytes = System.Text.Encoding.UTF8.GetBytes($"fake-surveillance-{mediaKind}-{id}");
        return Task.FromResult(new SurveillanceMediaPayload(
            bytes,
            mediaKind.Contains("clip", StringComparison.OrdinalIgnoreCase) ? "video/mp4" : "image/jpeg",
            $"{id}.{ (mediaKind.Contains("clip", StringComparison.OrdinalIgnoreCase) ? "mp4" : "jpg") }",
            mediaKind));
    }
}