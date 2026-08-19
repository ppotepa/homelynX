using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Health;

namespace TorrentBot.Plugins.System.Capabilities;

public sealed class HealthCapabilityHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
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
        var contributor=context.Engine.GetService<IHealthContributor>();
        if(contributor is not null)
        {
            var contribution=await contributor.CheckAsync(cancellationToken);
            data[contributor.Name]=new Dictionary<string,object?>{{"status",contribution.Status},{"detail",contribution.Detail}};
            if(contribution.Status!="healthy")data["status"]="degraded";
        }

        return new CapabilityResult(
            Success: true,
            Data: data,
            Message: data["status"]?.ToString()=="healthy"?"Engine is healthy":"Engine is degraded",
            IsDryRun: context.IsDryRun);
    }
}
