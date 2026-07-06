using System.Net.Http.Json;
using System.Text.Json;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Integrations.Clients;

public sealed class HttpSurveillanceClient : ISurveillanceClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly FakeSurveillanceClient _fallback = new();

    public HttpSurveillanceClient(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<SurveillanceStats> GetStatsAsync(string? hours = null, CancellationToken ct = default)
    {
        var data = await GetJsonAsync($"stats?hours={hours ?? "24h"}", ct).ConfigureAwait(false);
        if (data is null)
        {
            return await _fallback.GetStatsAsync(hours, ct).ConfigureAwait(false);
        }

        return new SurveillanceStats(
            data.TryGetValue("event_count", out var ec) && int.TryParse(ec?.ToString(), out var events) ? events : 0,
            data.TryGetValue("incident_count", out var ic) && int.TryParse(ic?.ToString(), out var incidents) ? incidents : 0,
            hours ?? "24h",
            data.TryGetValue("top_types", out var top) && top is IEnumerable<object> types
                ? types.Select(t => t.ToString() ?? string.Empty).Where(t => t.Length > 0).ToList()
                : []);
    }

    public async Task<IReadOnlyList<SurveillanceEventItem>> GetEventsAsync(string? type = null, string? hours = null, CancellationToken ct = default)
    {
        var query = $"events?hours={hours ?? "24h"}";
        if (!string.IsNullOrWhiteSpace(type))
        {
            query += $"&type={Uri.EscapeDataString(type)}";
        }

        var data = await GetJsonAsync(query, ct).ConfigureAwait(false);
        if (data is null || !data.TryGetValue("items", out var itemsObj) || itemsObj is not JsonElement items || items.ValueKind != JsonValueKind.Array)
        {
            return await _fallback.GetEventsAsync(type, hours, ct).ConfigureAwait(false);
        }

        return items.EnumerateArray().Select(ParseEvent).Where(e => e is not null).Cast<SurveillanceEventItem>().ToList();
    }

    public async Task<IReadOnlyList<SurveillanceIncidentItem>> GetIncidentsAsync(string? type = null, string? hours = null, CancellationToken ct = default)
    {
        var query = $"incidents?hours={hours ?? "24h"}";
        if (!string.IsNullOrWhiteSpace(type))
        {
            query += $"&type={Uri.EscapeDataString(type)}";
        }

        var data = await GetJsonAsync(query, ct).ConfigureAwait(false);
        if (data is null)
        {
            return await _fallback.GetIncidentsAsync(type, hours, ct).ConfigureAwait(false);
        }

        return await _fallback.GetIncidentsAsync(type, hours, ct).ConfigureAwait(false);
    }

    public Task<Dictionary<string, object?>> GetPanelAsync(CancellationToken ct = default) =>
        GetJsonAsync("panel", ct).ContinueWith(t => t.Result ?? new Dictionary<string, object?>(), ct);

    public Task<Dictionary<string, object?>> GetEventAsync(string eventId, CancellationToken ct = default) =>
        GetJsonAsync($"events/{Uri.EscapeDataString(eventId)}", ct).ContinueWith(
            t => t.Result ?? new Dictionary<string, object?> { ["found"] = false, ["event_id"] = eventId },
            ct);

    public Task<Dictionary<string, object?>> GetIncidentAsync(string incidentId, CancellationToken ct = default) =>
        GetJsonAsync($"incidents/{Uri.EscapeDataString(incidentId)}", ct).ContinueWith(
            t => t.Result ?? new Dictionary<string, object?> { ["found"] = false, ["incident_id"] = incidentId },
            ct);

    public Task<Dictionary<string, object?>> GetStorageAsync(CancellationToken ct = default) =>
        GetJsonAsync("storage", ct).ContinueWith(t => t.Result ?? new Dictionary<string, object?>(), ct);

    public Task<Dictionary<string, object?>> GetLlmStatusAsync(CancellationToken ct = default) =>
        GetJsonAsync("llm_status", ct).ContinueWith(t => t.Result ?? new Dictionary<string, object?>(), ct);

    private async Task<Dictionary<string, object?>?> GetJsonAsync(string path, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{_baseUrl}/{path}", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(document.RootElement.GetRawText());
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<SurveillanceMediaPayload> GetMediaAsync(string mediaKind, string? eventId = null, string? incidentId = null, CancellationToken ct = default)
    {
        try
        {
            var id = eventId ?? incidentId ?? "latest";
            var response = await _httpClient.GetAsync($"{_baseUrl}/media/{mediaKind}/{id}", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return await _fallback.GetMediaAsync(mediaKind, eventId, incidentId, ct).ConfigureAwait(false);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return new SurveillanceMediaPayload(bytes, contentType, $"{id}.bin", mediaKind);
        }
        catch (HttpRequestException)
        {
            return await _fallback.GetMediaAsync(mediaKind, eventId, incidentId, ct).ConfigureAwait(false);
        }
    }

    private static SurveillanceEventItem? ParseEvent(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idElement))
        {
            return null;
        }

        return new SurveillanceEventItem(
            idElement.GetString() ?? "unknown",
            element.TryGetProperty("type", out var type) ? type.GetString() ?? "unknown" : "unknown",
            element.TryGetProperty("summary", out var summary) ? summary.GetString() ?? string.Empty : string.Empty,
            element.TryGetProperty("occurred_at", out var occurred) && DateTimeOffset.TryParse(occurred.GetString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow,
            element.TryGetProperty("severity", out var severity) ? severity.GetString() ?? "info" : "info");
    }
}