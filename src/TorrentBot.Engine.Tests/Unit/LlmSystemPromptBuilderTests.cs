using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Repositories;
using TorrentBot.Llm;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class LlmSystemPromptBuilderTests
{
    [Fact]
    public void BuildPlannerPrompt_includes_capabilities_query_dsl_and_user_request()
    {
        var request = new LlmPlanningRequest(
            "list all commands",
            [
                new CapabilityMetadata(
                    "system.help",
                    "/help",
                    "Show available commands",
                    "USER",
                    RiskLevel.Safe,
                    LlmUsage: "Use when the user asks what commands are available",
                    IntentHints: ["help", "commands"])
            ],
            [
                new QuerySourceMeta(
                    "downloads",
                    "Unified download state",
                    [new QueryFieldMeta("status", "string")],
                    LlmUsage: "Inspect active downloads",
                    ExampleQueries: ["{ \"source\": \"downloads\" }"])
            ],
            Scope: "media");

        var prompt = LlmSystemPromptBuilder.BuildPlannerPrompt(request);

        Assert.Contains("system.help", prompt, StringComparison.Ordinal);
        Assert.Contains("/help", prompt, StringComparison.Ordinal);
        Assert.Contains("query.execute", prompt, StringComparison.Ordinal);
        Assert.Contains("downloads", prompt, StringComparison.Ordinal);
        Assert.Contains("list all commands", prompt, StringComparison.Ordinal);
        Assert.Contains("never a label, title, or query source name", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Query sources:", prompt, StringComparison.Ordinal);
    }
}