using Microsoft.Extensions.Logging;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Jobs;
using TorrentBot.Contracts.Query;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Contracts.Context;

public class CapabilityContext
{
    public required IEngineContext Engine { get; init; }
    public required IRequestContext Request { get; init; }
    public required UserContext User { get; init; }
    public string? JobId { get; init; }
    public bool IsDryRun { get; init; }
}

public interface IEngineContext
{
    void Publish<T>(T message) where T : class;
    IDisposable Subscribe<T>(Action<T> handler) where T : class;

    string CreateJob(string type, object payload, JobOptions? options = null);
    void UpdateJob(string jobId, Func<Job, Job> updater);
    Job? GetJob(string jobId);
    IReadOnlyList<Job> ListJobs();

    Task<QueryResult> QueryAsync(string source, QuerySpec spec, CancellationToken ct = default);
    IReadOnlyList<QuerySourceMeta> GetQuerySourceManifests();

    IReadOnlyList<CapabilityMetadata> GetAvailableCapabilities();
    CapabilityMetadata? GetCapability(string name);

    T? GetService<T>() where T : class;

    ILogger GetLogger(string category);
    string? CurrentTraceId { get; }
    CancellationToken CancellationToken { get; }
    bool IsDryRun { get; }
    UserContext CurrentUser { get; }
    IRequestContext RequestContext { get; }
    bool CanExecute(string capabilityName);
}