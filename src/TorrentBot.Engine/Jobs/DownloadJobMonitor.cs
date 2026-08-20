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
                if (string.IsNullOrWhiteSpace(processJobId) || string.IsNullOrWhiteSpace(status))
                {
                    continue;
                }

                foreach (var job in _jobTracker.GetAll())
                {
                    if (job.ExternalId != processJobId)
                    {
                        continue;
                    }

                    var nextStatus = status.ToLowerInvariant() switch
                    {
                        "completed" => JobStatus.Succeeded,
                        "failed" => JobStatus.Failed,
                        "cancelled" => JobStatus.Cancelled,
                        "paused" => JobStatus.Paused,
                        _ => JobStatus.Running
                    };
                    var nextProgress = nextStatus == JobStatus.Succeeded ? 1.0 :
                        row.TryGetValue("progress", out var progressValue) && double.TryParse(progressValue?.ToString(), out var progress)
                            ? Math.Clamp(progress, 0, 1)
                            : job.Progress;
                    var error = row.TryGetValue("error", out var errorValue) ? errorValue?.ToString() : null;

                    if (job.Status == nextStatus
                        && (nextStatus != JobStatus.Failed || string.Equals(job.Error, error, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    _jobTracker.Update(job.Id, current => current with
                    {
                        Status = nextStatus,
                        Progress = nextProgress,
                        Error = nextStatus == JobStatus.Failed ? error : current.Error,
                        Metadata = nextStatus == JobStatus.Succeeded
                            ? MergeArtifactMetadata(current.Metadata, row)
                            : current.Metadata
                    });
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string>? MergeArtifactMetadata(
        Dictionary<string, string>? metadata,
        IReadOnlyDictionary<string, object?> row)
    {
        if (!row.TryGetValue("outputPath", out var path) || string.IsNullOrWhiteSpace(path?.ToString()))
        {
            return metadata;
        }

        var result = metadata is null ? new Dictionary<string, string>() : new Dictionary<string, string>(metadata);
        result["ArtifactPath"] = path.ToString()!;
        if (row.TryGetValue("category", out var format) && !string.IsNullOrWhiteSpace(format?.ToString()))
        {
            result["MediaFormat"] = format.ToString()!;
        }

        return result;
    }

    public void Dispose() => Stop();
}
