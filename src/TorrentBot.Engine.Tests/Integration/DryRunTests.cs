using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class DryRunTests
{
    [Fact]
    public async Task Dry_run_invocation_does_not_persist_jobs_and_marks_result()
    {
        await using var scope = await EngineTestHelper.CreateStartedEngineAsync();
        var beforeCount = scope.Engine.ListJobs().Count;

        var result = await scope.Engine.SubmitAsync(EngineTestHelper.CreateInvocation(
            "test.echo",
            isDryRun: true,
            parameters: new Dictionary<string, object?> { ["createJob"] = true, ["message"] = "dry" }));

        Assert.True(result.IsDryRun);
        Assert.True(result.CapabilityResult!.IsDryRun);
        Assert.NotNull(result.CapabilityResult.JobId);
        Assert.StartsWith("dry-run-job-", result.CapabilityResult.JobId);
        Assert.Equal(beforeCount, scope.Engine.ListJobs().Count);
        Assert.Null(scope.Engine.GetJob(result.CapabilityResult.JobId!));
    }
}