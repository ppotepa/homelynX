using TorrentBot.Contracts.Jobs;
using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class ContextPropagationTests
{
    [Fact]
    public async Task Job_created_during_execution_carries_request_trace_and_user_ids()
    {
        await using var scope = await EngineTestHelper.CreateStartedEngineAsync();
        var request = EngineTestHelper.CreateRequestContext("trace-prop", "inv-prop", "user-prop", "test");
        var user = EngineTestHelper.CreateUserContext("user-prop");

        var result = await scope.Engine.SubmitAsync(EngineTestHelper.CreateInvocation(
            "test.echo",
            parameters: new Dictionary<string, object?> { ["createJob"] = true, ["message"] = "job" },
            requestContext: request,
            user: user));

        Assert.True(result.Success, "orchestrator submit must succeed for explicit test.echo invocation");
        Assert.NotNull(result.CapabilityResult?.JobId);

        var job = scope.Engine.GetJob(result.CapabilityResult!.JobId!);
        Assert.NotNull(job);
        Assert.Equal("trace-prop", job!.Metadata!["TraceId"]);
        Assert.Equal("inv-prop", job.Metadata["InvocationId"]);
        Assert.Equal("user-prop", job.Metadata["UserId"]);
        Assert.Equal("user-prop", job.OwnerUserId);
        Assert.Equal(JobStatus.Running, job.Status);
    }
}