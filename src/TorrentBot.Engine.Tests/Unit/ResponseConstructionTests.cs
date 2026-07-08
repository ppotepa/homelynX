using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Bus.Events;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine.Pipeline;
using TorrentBot.Engine.Pipeline.Behaviors;
using TorrentBot.Engine.Tests.Support;
using TorrentBot.Plugins.Downloads.Capabilities;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ResponseConstructionTests
{
    [Fact]
    public void ContractResponseConstructor_dispatches_to_registry_for_download_list()
    {
        var contract = DownloadContracts.List;
        var result = new ExecutionResult(
            Success: true,
            CapabilityResult: new CapabilityResult(
                Success: true,
                Data: new Dictionary<string, object?>
                {
                    ["downloads"] = new List<Dictionary<string, object?>>
                    {
                        new(StringComparer.Ordinal)
                        {
                            ["name"] = "ubuntu.iso",
                            ["status"] = "downloading",
                            ["progress"] = "42"
                        }
                    }
                }));

        var artifacts = new ContractResponseConstructor().Construct(contract, result);
        Assert.Single(artifacts);
        Assert.Contains("ubuntu.iso", ((TextArtifact)artifacts[0]).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseConstructionBehavior_replaces_pipeline_artifacts_using_contract_spec()
    {
        var contract = DownloadContracts.List;
        var raw = new ExecutionResult(
            Success: true,
            CapabilityResult: new CapabilityResult(
                Success: true,
                Data: new Dictionary<string, object?>
                {
                    ["downloads"] = new List<Dictionary<string, object?>>
                    {
                        new(StringComparer.Ordinal)
                        {
                            ["name"] = "file.mkv",
                            ["status"] = "paused",
                            ["progress"] = "10"
                        }
                    }
                },
                Message: "old handler message"));

        var behavior = new ResponseConstructionBehavior(new ContractResponseConstructor());
        var context = new PipelineBehaviorContext(
            EngineTestHelper.CreateInvocation(contract.Name),
            Conversation: null,
            Contracts: [contract],
            Capabilities: [],
            State: new Dictionary<string, object?>(StringComparer.Ordinal));

        var pipelineResult = new PipelineResult(
            true,
            ArtifactAccumulator.FromExecutionResult(raw),
            new ExecutionPlan(PlanSource.Deterministic, []),
            null);

        var (_, updated) = await behavior.AfterExecutionAsync(context, pipelineResult);

        Assert.NotNull(updated);
        Assert.Single(updated!.Artifacts.Items);
        var text = Assert.IsType<TextArtifact>(updated.Artifacts.Items[0]);
        Assert.Contains("file.mkv", text.Message, StringComparison.Ordinal);
        Assert.Contains("paused", text.Message, StringComparison.Ordinal);
        Assert.NotEqual("old handler message", text.Message);
    }

    [Fact]
    public async Task QueuedEventBus_dispatches_ResponseConstructedEvent_asynchronously()
    {
        await using var bus = new TorrentBot.Engine.Bus.QueuedEventBus();
        var ctx = EngineTestHelper.CreateRequestContext("trace-rc", "inv-rc", "user-rc");
        var received = new TaskCompletionSource<ResponseConstructedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var _ = bus.Subscribe<ResponseConstructedEvent>(message => received.TrySetResult(message.Payload));
        bus.Publish(new ResponseConstructedEvent("download.list", "list", true), ctx);

        var evt = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("download.list", evt.CapabilityName);
        Assert.Equal("list", evt.ArtifactKind);
        Assert.True(evt.Success);
    }
}