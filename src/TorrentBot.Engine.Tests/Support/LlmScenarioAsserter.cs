using TorrentBot.Contracts.Pipeline;

namespace TorrentBot.Engine.Tests.Support;

public static class LlmScenarioAsserter
{
    public static void AssertPlan(LlmScenarioDefinition scenario, ExecutionPlan plan)
    {
        var expect = scenario.Expect;

        if (plan.Steps.Count == 0)
        {
            if (expect.AllowEmptyPlan || expect.MinSteps == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"empty plan (intent={plan.Intent ?? "(none)"})");
        }

        Assert.InRange(plan.Steps.Count, Math.Max(1, expect.MinSteps), expect.MaxSteps);

        var first = plan.Steps[0].CapabilityName;
        if (!string.IsNullOrWhiteSpace(expect.FirstCapability))
        {
            Assert.True(
                string.Equals(expect.FirstCapability, first, StringComparison.OrdinalIgnoreCase),
                DescribeMismatch(scenario, plan, $"expected capability {expect.FirstCapability}, got {first}"));
        }

        if (expect.AllowedCapabilities.Count > 0)
        {
            Assert.True(
                expect.AllowedCapabilities.Contains(first, StringComparer.OrdinalIgnoreCase),
                DescribeMismatch(scenario, plan, $"expected one of [{string.Join(", ", expect.AllowedCapabilities)}], got {first}"));
        }

        if (expect.QueryContains.Count > 0 || expect.QueryNotContains.Count > 0)
        {
            var query = plan.Steps[0].Parameters?.TryGetValue("query", out var q) == true
                ? q?.ToString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrWhiteSpace(query))
            {
                throw new InvalidOperationException(DescribeMismatch(scenario, plan, "expected non-empty query parameter"));
            }

            foreach (var token in expect.QueryContains)
            {
                Assert.True(
                    query.Contains(token, StringComparison.OrdinalIgnoreCase),
                    DescribeMismatch(scenario, plan, $"query should contain '{token}', got '{query}'"));
            }

            if (string.Equals(scenario.Category, "query_quality", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var token in expect.QueryNotContains)
                {
                    Assert.False(
                        query.Contains(token, StringComparison.OrdinalIgnoreCase),
                        DescribeMismatch(scenario, plan, $"query should not contain '{token}', got '{query}'"));
                }
            }
        }
    }

    private static string DescribeMismatch(LlmScenarioDefinition scenario, ExecutionPlan plan, string message)
    {
        var step = plan.Steps[0];
        var query = step.Parameters?.TryGetValue("query", out var q) == true ? q?.ToString() : null;
        return $"[{scenario.Id}] {scenario.Input}: {message} | plan={step.CapabilityName} query={query ?? "-"} intent={plan.Intent}";
    }
}