using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Plugins.BotControl.Capabilities;

public sealed class BotDiagHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["engine"] = "running",
                ["plugins"] = context.Engine.GetAvailableCapabilities().Count,
                ["query_sources"] = context.Engine.GetQuerySourceManifests().Count,
                ["trace_id"] = context.Request.TraceId
            },
            Message: "Bot diagnostics loaded.",
            IsDryRun: context.IsDryRun));
}

public sealed class BotPluginsHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["hot_reload"] = false,
                ["plugins"] = context.Engine.GetAvailableCapabilities()
                    .Select(c => c.Name)
                    .OrderBy(n => n)
                    .ToList()
            },
            Message: "Registered plugins listed.",
            IsDryRun: context.IsDryRun));
}

public sealed class BotPluginsReloadHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?> { ["reloaded"] = !context.IsDryRun },
            Message: context.IsDryRun ? "Dry-run: plugins would reload." : "Hot plugin reload is disabled in C# engine.",
            IsDryRun: context.IsDryRun));
}

public sealed class CoordStatusHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var client = context.Engine.GetService<ICoordInputClient>();
        if (client is null)
        {
            return new CapabilityResult(Success: false, Message: "Coord client is not available.", IsDryRun: context.IsDryRun);
        }

        var status = await client.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["online"] = status.Online,
                ["service"] = status.Service,
                ["pending_inputs"] = status.PendingInputs,
                ["checked_at"] = status.CheckedAtUtc
            },
            Message: $"Coord service {(status.Online ? "online" : "offline")}.",
            IsDryRun: context.IsDryRun);
    }
}

public sealed class CoordSubmitHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var client = context.Engine.GetService<ICoordInputClient>();
        if (client is null)
        {
            return new CapabilityResult(Success: false, Message: "Coord client is not available.", IsDryRun: context.IsDryRun);
        }

        if (!double.TryParse(Get(parameters, "lat") ?? Get(parameters, "latitude"), out var lat)
            || !double.TryParse(Get(parameters, "lon") ?? Get(parameters, "longitude"), out var lon))
        {
            return new CapabilityResult(Success: false, Message: "Parameters 'lat' and 'lon' are required.", IsDryRun: context.IsDryRun);
        }

        var result = await client.SubmitAsync(lat, lon, Get(parameters, "label"), cancellationToken).ConfigureAwait(false);
        return new CapabilityResult(
            Success: result.Accepted,
            Data: new Dictionary<string, object?> { ["tracking_id"] = result.TrackingId, ["message"] = result.Message },
            Message: result.Message ?? "Coordinate submitted.",
            IsDryRun: context.IsDryRun);
    }

    private static string? Get(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}