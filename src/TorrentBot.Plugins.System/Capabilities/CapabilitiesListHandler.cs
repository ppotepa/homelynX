using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Plugins.System.Capabilities;

public sealed class CapabilitiesListHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var filter = GetString(parameters, "filter")
                     ?? GetString(parameters, "category")
                     ?? GetString(parameters, "module")
                     ?? GetString(parameters, "search");

        var source = context.Engine.GetAvailableCapabilities()
            .Where(c => context.Engine.CanExecute(c.Name));

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.ToLowerInvariant();
            source = source.Where(c =>
            {
                var name = c.Name?.ToLowerInvariant() ?? "";
                var cmd = c.Command?.ToLowerInvariant() ?? "";
                var desc = c.Description?.ToLowerInvariant() ?? "";
                var module = name.Contains('.') ? name.Split('.')[0] : name;
                var hints = (c.IntentHints != null) ? string.Join(" ", c.IntentHints).ToLowerInvariant() : "";
                return name.Contains(f) || cmd.Contains(f) || desc.Contains(f) || module.Contains(f) || hints.Contains(f);
            });
        }

        var capabilities = source
            .Select(c => new Dictionary<string, object?>
            {
                ["name"] = c.Name,
                ["command"] = c.Command,
                ["description"] = c.Description,
                ["permission"] = c.Permission,
                ["risk"] = c.Risk.ToString()
            })
            .ToList();

        var msgFilter = string.IsNullOrWhiteSpace(filter) ? "" : $" (filtered by '{filter}')";
        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?> { ["capabilities"] = capabilities, ["count"] = capabilities.Count, ["filter"] = filter },
            Message: $"{capabilities.Count} capability(ies) available{msgFilter}",
            IsDryRun: context.IsDryRun));
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}