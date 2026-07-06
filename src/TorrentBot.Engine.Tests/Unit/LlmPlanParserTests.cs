using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Llm;
using TorrentBot.Llm;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class LlmPlanParserTests
{
    [Fact]
    public void TryParse_filters_unknown_capabilities_from_llm_output()
    {
        var request = new LlmPlanningRequest(
            "list all commands",
            [new CapabilityMetadata("system.help", "/help", "help", "USER", RiskLevel.Safe)],
            []);

        var ok = LlmPlanParser.TryParse(
            """
            {
              "intent": "list commands",
              "steps": [
                { "capability": "Query sources", "parameters": {}, "why": "bad" },
                { "capability": "system.help", "parameters": {}, "why": "good" }
              ],
              "confidence": 0.9
            }
            """,
            request,
            out var plan);

        Assert.True(ok);
        Assert.Single(plan.Steps);
        Assert.Equal("system.help", plan.Steps[0].Capability);
    }
}