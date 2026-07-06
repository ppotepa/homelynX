using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Jobs;

namespace TorrentBot.Plugins.System.Capabilities;

public sealed class StatusCapabilityHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var jobs = context.Engine.ListJobs();
        var manifests = context.Engine.GetQuerySourceManifests();

        var data = new Dictionary<string, object?>
        {
            ["engine"] = "running",
            ["activeJobs"] = jobs.Count(j => j.Status is JobStatus.Running or JobStatus.Queued),
            ["totalJobs"] = jobs.Count,
            ["querySources"] = manifests.Select(m => m.Name).ToArray(),
            ["userId"] = context.User.UserId,
            ["profile"] = context.User.EffectiveProfile,
            ["dryRun"] = context.IsDryRun
        };

        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: data,
            Message: $"Engine running with {jobs.Count} tracked job(s)",
            IsDryRun: context.IsDryRun));
    }
}