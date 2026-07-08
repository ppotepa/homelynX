using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;

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

