using TorrentBot.Contracts.Jobs;
using TorrentBot.Engine.Bus;

namespace TorrentBot.Engine.Jobs;

public sealed record DownloadCompletedEvent(string JobId, string Name, string Provider, string OwnerUserId, string? ChatId);

public interface IJobRunner
{
    void Start(IJobTracker jobTracker, IInternalBus bus, CancellationToken cancellationToken = default);
    void Stop();
}

public sealed class BackgroundJobRunner : IJobRunner, IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public void Start(IJobTracker jobTracker, IInternalBus bus, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_loop is not null)
            {
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _loop = Task.Run(() => RunAsync(jobTracker, bus, _cts.Token), _cts.Token);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _loop = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static async Task RunAsync(IJobTracker jobTracker, IInternalBus bus, CancellationToken ct)
    {
        var seenCompleted = new HashSet<string>(StringComparer.Ordinal);
        while (!ct.IsCancellationRequested)
        {
            foreach (var job in jobTracker.GetAll())
            {
                if (job.Status != JobStatus.Succeeded || !seenCompleted.Add(job.Id))
                {
                    continue;
                }

                var chatId = job.Metadata?.GetValueOrDefault("ChatId");
                var owner = job.OwnerUserId ?? job.Metadata?.GetValueOrDefault("UserId") ?? "unknown";
                bus.Publish(
                    new DownloadCompletedEvent(job.Id, job.Type, job.ExternalSystem ?? "download", owner, chatId),
                    new TorrentBot.Contracts.Context.RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), owner, source: "job-runner", chatId: chatId));
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
    }

    public void Dispose() => Stop();
}