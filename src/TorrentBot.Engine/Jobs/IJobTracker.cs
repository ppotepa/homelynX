using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Jobs;

namespace TorrentBot.Engine.Jobs;

public interface IJobTracker
{
    string Create(string type, object payload, JobOptions? options, IRequestContext? ctx = null);
    void Update(string jobId, Func<Job, Job> updater);
    Job? Get(string jobId);
    IReadOnlyCollection<Job> GetAll();
}