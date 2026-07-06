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
        var capabilities = context.Engine.GetAvailableCapabilities()
            .Where(c => context.Engine.CanExecute(c.Name))
            .Select(c => new Dictionary<string, object?>
            {
                ["name"] = c.Name,
                ["command"] = c.Command,
                ["description"] = c.Description,
                ["permission"] = c.Permission,
                ["risk"] = c.Risk.ToString()
            })
            .ToList();

        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?> { ["capabilities"] = capabilities, ["count"] = capabilities.Count },
            Message: $"{capabilities.Count} capability(ies) available",
            IsDryRun: context.IsDryRun));
    }
}