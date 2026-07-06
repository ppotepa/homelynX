using TorrentBot.Contracts.Context;

namespace TorrentBot.Contracts.ProcessManagers;

public interface IProcessManager
{
    string ProcessType { get; }
    Task<string> StartAsync(object startPayload, IRequestContext context, CancellationToken ct = default);
    Task HandleCommandAsync(string jobId, string command, object? payload, IRequestContext actorContext, CancellationToken ct = default);
}

public interface IDownloadProcessManager : IProcessManager
{
    IReadOnlyList<Dictionary<string, object?>> GetTrackedProcessRows();
    Task SyncDownloadStatusesAsync(CancellationToken ct = default);
}