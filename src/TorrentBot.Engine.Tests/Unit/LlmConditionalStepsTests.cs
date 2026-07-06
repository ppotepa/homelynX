using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Llm;
using TorrentBot.Llm;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class LlmConditionalStepsTests
{
    [Fact]
    public void Stub_executor_skips_steps_when_saved_condition_not_met()
    {
        var executor = new StubLlmExecutor();
        var plan = new PlanEnvelope(
            "conditional",
            [
                new PlanStep("system.health", SaveAs: "healthData"),
                new PlanStep("query.execute", Condition: "saved:healthData", Parameters: new Dictionary<string, object?> { ["source"] = "downloads" }),
                new PlanStep("torrent.search", Condition: "saved:missingKey")
            ]);

        var result = executor.Execute(new LlmExecutionRequest(
            plan,
            [
                new CapabilityMetadata("system.health", null, "", "USER", RiskLevel.Safe),
                new CapabilityMetadata("query.execute", null, "", "USER", RiskLevel.Safe),
                new CapabilityMetadata("torrent.search", null, "", "USER", RiskLevel.Safe)
            ]));

        Assert.True(result.Success);
        Assert.Equal(2, result.StepsToExecute.Count);
        Assert.Equal("system.health", result.StepsToExecute[0].Capability);
        Assert.Equal("query.execute", result.StepsToExecute[1].Capability);
        Assert.Contains(result.StepResults, s => s.Skipped && s.Step.Capability == "torrent.search");
    }
}