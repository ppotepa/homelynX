using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Jobs;

namespace TorrentBot.Engine.Jobs;

public sealed class InMemoryJobTracker : IJobTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Job> _jobs = new();

    public string Create(string type, object payload, JobOptions? options, IRequestContext? ctx = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(payload);

        options ??= new JobOptions();
        var now = DateTimeOffset.UtcNow;
        var jobId = $"job-{Guid.NewGuid():N}";

        var metadata = new Dictionary<string, string>();
        if (ctx is not null)
        {
            metadata["TraceId"] = ctx.TraceId;
            metadata["InvocationId"] = ctx.InvocationId;
            metadata["UserId"] = ctx.UserId;
            metadata["Source"] = ctx.Source;
            if (!string.IsNullOrWhiteSpace(ctx.ChatId))
            {
                metadata["ChatId"] = ctx.ChatId;
            }
        }

        var job = new Job(
            Id: jobId,
            Type: type,
            Kind: options.Kind,
            Payload: payload,
            Status: JobStatus.Queued,
            Progress: 0,
            Result: null,
            Error: null,
            ExternalId: null,
            ExternalSystem: null,
            SupportsCancellation: options.SupportsCancellation,
            SupportsPause: options.SupportsPause,
            EstimatedTotalDuration: null,
            ExpiresAt: options.Ttl is { } ttl ? now.Add(ttl) : null,
            Metadata: metadata,
            CreatedAt: now,
            UpdatedAt: now,
            ParentJobId: null,
            OwnerUserId: ctx?.UserId);

        lock (_gate)
        {
            _jobs[jobId] = job;
        }

        return jobId;
    }

    public void Update(string jobId, Func<Job, Job> updater)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(updater);

        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var job))
            {
                throw new KeyNotFoundException($"Job '{jobId}' was not found.");
            }

            _jobs[jobId] = updater(job) with { UpdatedAt = DateTimeOffset.UtcNow };
        }
    }

    public Job? Get(string jobId)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(jobId, out var job) ? job : null;
        }
    }

    public IReadOnlyCollection<Job> GetAll()
    {
        lock (_gate)
        {
            return _jobs.Values.ToList();
        }
    }
}