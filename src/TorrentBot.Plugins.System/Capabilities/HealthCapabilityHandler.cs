using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Plugins.System.Capabilities;

public sealed class HealthCapabilityHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, object?>
        {
            ["status"] = "healthy",
            ["engine"] = "running",
            ["dryRun"] = context.IsDryRun,
            ["traceId"] = context.Request.TraceId,
            ["timestampUtc"] = DateTimeOffset.UtcNow
        };

        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: data,
            Message: "Engine is healthy",
            IsDryRun: context.IsDryRun));
    }
}