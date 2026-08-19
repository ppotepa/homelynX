using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Tools;

internal static class TrackingTools
{
    public static async Task<CapabilityResult> ExecuteAsync(string input, string userId, FeatureStore store, CancellationToken ct)
    {
        var (command, rest) = Split(input);
        return command switch
        {
            "add" => await Add(rest, userId, store),
            "list" => await List(userId, store),
            "show" or "status" => await Show(rest, userId, store),
            "refresh" => await Refresh(rest, userId, store, ct),
            "pause" => await SetActive(rest, userId, store, false),
            "resume" => await SetActive(rest, userId, store, true),
            "delete" or "remove" => await Delete(rest, userId, store),
            _ => await Add(input, userId, store)
        };
    }

    private static async Task<CapabilityResult> Add(string input, string userId, FeatureStore store)
    {
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Usage("/track add NUMBER carrier=auto label=\"My parcel\" notify=important");
        var number = parts[0].Trim();
        if (number.Length is < 5 or > 64 || number.Any(char.IsWhiteSpace)) return Usage("Tracking number must be 5-64 characters.");
        var options = ParseOptions(input);
        var carrier = options.GetValueOrDefault("carrier", "auto");
        var label = options.GetValueOrDefault("label", "");
        var notify = options.GetValueOrDefault("notify", "important");
        if (notify is not ("all" or "important" or "delivery")) return Usage("notify must be all, important or delivery.");
        var provider = TrackingProvider.Configured ? "aftership" : "manual";
        var id = await store.AddShipmentAsync(userId, number, carrier, provider, label, notify);
        return Ok($"Tracking #{id} added: {number}\nProvider: {provider}\nUse /track refresh {id} to check now.");
    }

    private static async Task<CapabilityResult> List(string userId, FeatureStore store)
    {
        var shipments = await store.ListShipmentsAsync(userId);
        return Ok(shipments.Length == 0 ? "No tracked parcels." : string.Join('\n', shipments.Select(x => $"#{x.Id} {x.Number} — {x.Status}{(string.IsNullOrWhiteSpace(x.Label) ? "" : $" [{x.Label}]")}{(x.Active ? "" : " PAUSED")}")));
    }

    private static async Task<CapabilityResult> Show(string input, string userId, FeatureStore store)
    {
        var shipment = await Find(input, userId, store);
        if (shipment is null) return Ok("Shipment not found.");
        return Ok($"#{shipment.Id} {shipment.Number}\nStatus: {shipment.Status}\n{shipment.LastEvent}\nLast checked: {shipment.LastChecked?.ToLocalTime():yyyy-MM-dd HH:mm}");
    }

    private static async Task<CapabilityResult> Refresh(string input, string userId, FeatureStore store, CancellationToken ct)
    {
        var shipment = await Find(input, userId, store);
        if (shipment is null) return Ok("Shipment not found.");
        var update = await TrackingProvider.GetAsync(shipment, ct);
        if (update is null) return Ok(ManualLink(shipment));
        var changed = !string.Equals(shipment.StatusHash, update.RawHash, StringComparison.Ordinal);
        var delivered = update.Status.Contains("delivered", StringComparison.OrdinalIgnoreCase);
        await store.UpdateShipmentAsync(shipment.Id, update.Status, update.RawHash, FormatEvent(update), delivered, changed, DateTimeOffset.UtcNow.AddHours(1));
        return Ok(changed ? $"Shipment updated: {update.Status}\n{update.Description}" : $"No change: {update.Status}\n{update.Description}");
    }

    private static async Task<CapabilityResult> SetActive(string input, string userId, FeatureStore store, bool active)
    {
        var shipment = await Find(input, userId, store);
        if (shipment is null) return Ok("Shipment not found.");
        await store.SetShipmentActiveAsync(userId, shipment.Id, active);
        return Ok($"Tracking #{shipment.Id} {(active ? "resumed" : "paused")}.");
    }

    private static async Task<CapabilityResult> Delete(string input, string userId, FeatureStore store)
    {
        var shipment = await Find(input, userId, store);
        if (shipment is null) return Ok("Shipment not found.");
        await store.DeleteShipmentAsync(userId, shipment.Id);
        return Ok($"Tracking #{shipment.Id} deleted.");
    }

    private static async Task<ShipmentRecord?> Find(string input, string userId, FeatureStore store) =>
        long.TryParse(input.Trim(), out var id) ? await store.GetShipmentAsync(userId, id) : await store.GetShipmentByNumberAsync(userId, input.Trim());

    private static string ManualLink(ShipmentRecord shipment) => $"No tracking provider is configured. Search manually: https://www.google.com/search?q={Uri.EscapeDataString(shipment.Number)}";
    private static string FormatEvent(TrackingUpdate update) => string.IsNullOrWhiteSpace(update.Location) ? update.Description : $"{update.Description} ({update.Location})";
    private static Dictionary<string, string> ParseOptions(string input) => System.Text.RegularExpressions.Regex.Matches(input, "(?<key>[a-zA-Z][a-zA-Z0-9_]*)=(?<value>\\\"[^\\\"]*\\\"|'[^']*'|[^ ]+)").Cast<System.Text.RegularExpressions.Match>().ToDictionary(x => x.Groups["key"].Value, x => x.Groups["value"].Value.Trim('"', '\''), StringComparer.OrdinalIgnoreCase);
    private static (string Command, string Remainder) Split(string input) { var p = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries); return p.Length == 0 ? ("", "") : (p[0].ToLowerInvariant(), p.ElementAtOrDefault(1) ?? ""); }
    private static CapabilityResult Ok(string message) => new(true, message, message);
    private static CapabilityResult Usage(string message) => new(true, null, message);
}

public sealed record TrackingUpdate(string Status, string Description, string Location, DateTimeOffset? EventAt, string RawHash);

internal static class TrackingProvider
{
    private static string? ApiKey => Environment.GetEnvironmentVariable("AFTERSHIP_API_KEY");
    public static bool Configured => !string.IsNullOrWhiteSpace(ApiKey);

    public static async Task<TrackingUpdate?> GetAsync(ShipmentRecord shipment, CancellationToken ct)
    {
        if (!Configured) return null;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Add("as-api-key", ApiKey);
        var slug = shipment.Carrier.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "" : "/" + Uri.EscapeDataString(shipment.Carrier);
        using var response = await client.GetAsync($"https://api.aftership.com/tracking/2026-07/trackings{slug}/{Uri.EscapeDataString(shipment.Number)}", ct);
        if (!response.IsSuccessStatusCode) return null;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var tracking = document.RootElement.GetProperty("data").GetProperty("tracking");
        var status = tracking.TryGetProperty("tag", out var tag) ? tag.GetString() ?? "unknown" : "unknown";
        var checkpoints = tracking.TryGetProperty("checkpoints", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().LastOrDefault() : default;
        var description = checkpoints.ValueKind == JsonValueKind.Object && checkpoints.TryGetProperty("message", out var message) ? message.GetString() ?? status : status;
        var location = checkpoints.ValueKind == JsonValueKind.Object && checkpoints.TryGetProperty("location", out var place) ? place.GetString() ?? "" : "";
        var raw = tracking.GetRawText();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        DateTimeOffset? eventAt = null;
        if (checkpoints.ValueKind == JsonValueKind.Object && checkpoints.TryGetProperty("checkpoint_time", out var time) && DateTimeOffset.TryParse(time.GetString(), out var parsed)) eventAt = parsed;
        return new TrackingUpdate(status, description, location, eventAt, hash);
    }
}

public sealed class TrackingMonitor
{
    private readonly FeatureStore _store;
    public TrackingMonitor(FeatureStore store) => _store = store;

    public async Task RunAsync(Func<ShipmentRecord, TrackingUpdate, Task> notify, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var shipment in await _store.DueShipmentsAsync(DateTimeOffset.UtcNow))
            {
                try
                {
                    var update = await TrackingProvider.GetAsync(shipment, ct);
                    if (update is null) continue;
                    var changed = !string.Equals(shipment.StatusHash, update.RawHash, StringComparison.Ordinal);
                    var delivered = update.Status.Contains("delivered", StringComparison.OrdinalIgnoreCase);
                    await _store.UpdateShipmentAsync(shipment.Id, update.Status, update.RawHash, string.IsNullOrWhiteSpace(update.Location) ? update.Description : $"{update.Description} ({update.Location})", delivered, changed, DateTimeOffset.UtcNow.AddHours(1));
                    if (changed) await notify(shipment, update);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch { }
            }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
