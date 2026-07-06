using TorrentBot.Contracts.Bus;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Jobs;
using TorrentBot.Contracts.Plugins;
using TorrentBot.Contracts.Query;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Engine;

public interface IEngine
{
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    void RegisterPlugin(IPlugin plugin);
    Task<ExecutionResult> SubmitAsync(Invocation invocation, CancellationToken cancellationToken = default);

    Job? GetJob(string jobId);
    IReadOnlyList<Job> ListJobs();
    IDisposable Subscribe<T>(Action<CorrelatedMessage<T>> handler) where T : class;
    IReadOnlyList<QuerySourceMeta> GetQuerySourceManifests();
    Task<QueryResult> QueryAsync(string source, QuerySpec spec, CancellationToken ct = default);
}