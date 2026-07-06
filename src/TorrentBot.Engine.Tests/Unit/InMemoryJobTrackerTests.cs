using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Jobs;
using TorrentBot.Engine.Jobs;
using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class InMemoryJobTrackerTests
{
    [Fact]
    public void Create_attaches_request_correlation_metadata()
    {
        var tracker = new InMemoryJobTracker();
        var ctx = EngineTestHelper.CreateRequestContext("trace-abc", "inv-def", "user-ghi", "cli");

        var jobId = tracker.Create("test.job", new { x = 1 }, new JobOptions(), ctx);
        var job = tracker.Get(jobId);

        Assert.NotNull(job);
        Assert.Equal("trace-abc", job.Metadata!["TraceId"]);
        Assert.Equal("inv-def", job.Metadata["InvocationId"]);
        Assert.Equal("user-ghi", job.Metadata["UserId"]);
        Assert.Equal("cli", job.Metadata["Source"]);
        Assert.Equal("user-ghi", job.OwnerUserId);
    }

    [Fact]
    public void Update_replaces_job_immutably()
    {
        var tracker = new InMemoryJobTracker();
        var jobId = tracker.Create("test.job", new { }, null, null);

        tracker.Update(jobId, job => job with { Status = JobStatus.Running, Progress = 0.75 });

        var job = tracker.Get(jobId);
        Assert.Equal(JobStatus.Running, job!.Status);
        Assert.Equal(0.75, job.Progress);
    }
}