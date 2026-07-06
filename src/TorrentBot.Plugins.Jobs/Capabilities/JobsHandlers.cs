using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Jobs;

namespace TorrentBot.Plugins.Jobs.Capabilities;

public sealed class JobsListHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var jobs = context.Engine.ListJobs()
            .Select(job => new Dictionary<string, object?>
            {
                ["id"] = job.Id,
                ["type"] = job.Type,
                ["status"] = job.Status.ToString(),
                ["progress"] = job.Progress,
                ["ownerUserId"] = job.OwnerUserId,
                ["externalId"] = job.ExternalId
            })
            .ToList();

        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?> { ["jobs"] = jobs, ["count"] = jobs.Count },
            Message: $"Listed {jobs.Count} job(s).",
            IsDryRun: context.IsDryRun));
    }
}

public sealed class JobsCancelHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var jobId = GetString(parameters, "jobId") ?? GetString(parameters, "id");
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return Task.FromResult(new CapabilityResult(Success: false, Message: "Parameter 'jobId' is required."));
        }

        var job = context.Engine.GetJob(jobId);
        if (job is null)
        {
            return Task.FromResult(new CapabilityResult(Success: false, Message: $"Job '{jobId}' was not found."));
        }

        if (context.IsDryRun)
        {
            return Task.FromResult(new CapabilityResult(
                Success: true,
                Message: $"Dry-run: would cancel job '{jobId}'",
                IsDryRun: true));
        }

        context.Engine.UpdateJob(jobId, current => current with { Status = JobStatus.Cancelled, Progress = current.Progress });
        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?> { ["jobId"] = jobId, ["status"] = "cancelled" },
            Message: $"Cancelled job '{jobId}'."));
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}