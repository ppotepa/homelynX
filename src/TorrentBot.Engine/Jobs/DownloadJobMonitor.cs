using TorrentBot.Contracts.Jobs;
using TorrentBot.Contracts.ProcessManagers;

namespace TorrentBot.Engine.Jobs;

public sealed class DownloadJobMonitor : IDisposable
{
    private readonly IJobTracker _jobTracker;
    private readonly IDownloadProcessManager _processManager;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public DownloadJobMonitor(IJobTracker jobTracker, IDownloadProcessManager processManager)
    {
        _jobTracker = jobTracker;
        _processManager = processManager;
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        if (_loop is not null)
        {
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _loop = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _processManager.SyncDownloadStatusesAsync(ct).ConfigureAwait(false);
            var processRows = _processManager.GetTrackedProcessRows();
            foreach (var row in processRows)
            {
                var processJobId = row.TryGetValue("id", out var id) ? id?.ToString() : null;
                var status = row.TryGetValue("status", out var statusValue) ? statusValue?.ToString() : null;
                if (string.IsNullOrWhiteSpace(processJobId) || !string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var job in _jobTracker.GetAll())
                {
                    if (job.ExternalId == processJobId && job.Status != JobStatus.Succeeded)
                    {
                        _jobTracker.Update(job.Id, current => current with
                        {
                            Status = JobStatus.Succeeded,
                            Progress = 1.0
                        });
                    }
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    public void Dispose() => Stop();
}