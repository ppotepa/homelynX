namespace TorrentBot.Integrations.Interfaces;

public sealed record SurveillanceStats(int EventCount, int IncidentCount, string Window, IReadOnlyList<string> TopTypes);

public sealed record SurveillanceEventItem(string Id, string Type, string Summary, DateTimeOffset OccurredAt, string Severity);

public sealed record SurveillanceIncidentItem(string Id, string Type, string Summary, DateTimeOffset OpenedAt, string Priority);

public sealed record SurveillanceMediaPayload(byte[] Content, string ContentType, string FileName, string MediaKind);

public interface ISurveillanceClient
{
    Task<SurveillanceStats> GetStatsAsync(string? hours = null, CancellationToken ct = default);
    Task<IReadOnlyList<SurveillanceEventItem>> GetEventsAsync(string? type = null, string? hours = null, CancellationToken ct = default);
    Task<IReadOnlyList<SurveillanceIncidentItem>> GetIncidentsAsync(string? type = null, string? hours = null, CancellationToken ct = default);
    Task<Dictionary<string, object?>> GetPanelAsync(CancellationToken ct = default);
    Task<Dictionary<string, object?>> GetEventAsync(string eventId, CancellationToken ct = default);
    Task<Dictionary<string, object?>> GetIncidentAsync(string incidentId, CancellationToken ct = default);
    Task<Dictionary<string, object?>> GetStorageAsync(CancellationToken ct = default);
    Task<Dictionary<string, object?>> GetLlmStatusAsync(CancellationToken ct = default);
    Task<SurveillanceMediaPayload> GetMediaAsync(string mediaKind, string? eventId = null, string? incidentId = null, CancellationToken ct = default);
}