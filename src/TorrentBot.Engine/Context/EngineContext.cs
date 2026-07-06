using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TorrentBot.Contracts.Bus;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Jobs;
using TorrentBot.Contracts.Query;
using TorrentBot.Contracts.Repositories;
using TorrentBot.Engine.Bus;
using TorrentBot.Engine.Capabilities;
using TorrentBot.Engine.Jobs;
using TorrentBot.Engine.Repositories;

namespace TorrentBot.Engine.Context;

public sealed class EngineContext : IEngineContext
{
    private readonly IInternalBus _bus;
    private readonly IJobTracker _jobTracker;
    private readonly CapabilityRegistry _capabilities;
    private readonly RepositoryAggregator _repositories;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Func<Type, object?> _serviceResolver;
    private readonly bool _dryRunSkipsPersistence;
    private readonly Func<UserContext, CapabilityMetadata, bool>? _canExecute;

    public EngineContext(
        IInternalBus bus,
        IJobTracker jobTracker,
        CapabilityRegistry capabilities,
        RepositoryAggregator repositories,
        IRequestContext requestContext,
        UserContext currentUser,
        bool isDryRun,
        CancellationToken cancellationToken,
        ILoggerFactory loggerFactory,
        Func<Type, object?> serviceResolver,
        bool dryRunSkipsPersistence = true,
        Func<UserContext, CapabilityMetadata, bool>? canExecute = null)
    {
        _bus = bus;
        _jobTracker = jobTracker;
        _capabilities = capabilities;
        _repositories = repositories;
        RequestContext = requestContext;
        CurrentUser = currentUser;
        IsDryRun = isDryRun;
        CancellationToken = cancellationToken;
        _loggerFactory = loggerFactory;
        _serviceResolver = serviceResolver;
        _dryRunSkipsPersistence = dryRunSkipsPersistence;
        _canExecute = canExecute;
    }

    public IRequestContext RequestContext { get; }
    public UserContext CurrentUser { get; }
    public bool IsDryRun { get; }
    public CancellationToken CancellationToken { get; }
    public string? CurrentTraceId => RequestContext.TraceId;

    public void Publish<T>(T message) where T : class =>
        _bus.Publish(message, RequestContext);

    public IDisposable Subscribe<T>(Action<T> handler) where T : class
    {
        return _bus.Subscribe<T>(correlated => handler(correlated.Payload));
    }

    public string CreateJob(string type, object payload, JobOptions? options = null)
    {
        if (IsDryRun && _dryRunSkipsPersistence)
        {
            return $"dry-run-job-{Guid.NewGuid():N}";
        }

        return _jobTracker.Create(type, payload, options, RequestContext);
    }

    public void UpdateJob(string jobId, Func<Job, Job> updater)
    {
        if (IsDryRun && _dryRunSkipsPersistence)
        {
            return;
        }

        _jobTracker.Update(jobId, updater);
    }

    public Job? GetJob(string jobId)
    {
        if (IsDryRun && _dryRunSkipsPersistence && jobId.StartsWith("dry-run-job-", StringComparison.Ordinal))
        {
            return null;
        }

        return _jobTracker.Get(jobId);
    }

    public IReadOnlyList<Job> ListJobs() => _jobTracker.GetAll().ToList();

    public Task<QueryResult> QueryAsync(string source, QuerySpec spec, CancellationToken ct = default) =>
        _repositories.QueryAsync(source, spec, ct);

    public IReadOnlyList<QuerySourceMeta> GetQuerySourceManifests() =>
        _repositories.GetManifests();

    public IReadOnlyList<CapabilityMetadata> GetAvailableCapabilities() =>
        _capabilities.GetAllMetadata();

    public CapabilityMetadata? GetCapability(string name) =>
        _capabilities.GetMetadata(name);

    public T? GetService<T>() where T : class =>
        (T?)_serviceResolver(typeof(T));

    public ILogger GetLogger(string category) =>
        _loggerFactory.CreateLogger(category);

    public bool CanExecute(string capabilityName)
    {
        var metadata = _capabilities.GetMetadata(capabilityName);
        return metadata is not null && (_canExecute?.Invoke(CurrentUser, metadata) ?? true);
    }
}