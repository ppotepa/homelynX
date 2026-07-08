using TorrentBot.Acl;
using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine.Pipeline;
using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Integration;

[CollectionDefinition("LlmLive", DisableParallelization = true)]
public sealed class LlmLiveCollection;

/// <summary>
/// 100 live Ollama planner scenarios. Requires TORRENTBOT_RUN_LLM_TESTS=true and reachable LLM (Ollama).
/// </summary>
[Collection("LlmLive")]
public sealed class LlmPlannerScenarioTests
{
    private static readonly IReadOnlyList<LlmScenarioDefinition> Scenarios = LlmScenarioCatalog.LoadAll();
    private static readonly bool Enabled = LlmTestEnvironment.IsEnabled;
    private static readonly Lazy<Task<bool>> Reachable = new(() => LlmTestEnvironment.IsLivePlannerReachableAsync());

    public static IEnumerable<object[]> ScenarioData
    {
        get
        {
            if (!Enabled)
            {
                return [];
            }

            var filter = Environment.GetEnvironmentVariable("TORRENTBOT_LLM_SCENARIO_FILTER");
            var selected = Scenarios.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(filter))
            {
                selected = selected.Where(s =>
                    s.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || s.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || s.Input.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            return selected.Select(s => new object[] { s });
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioData))]
    public async Task Live_planner_scenario(LlmScenarioDefinition scenario)
    {
        if (!Enabled)
        {
            return;
        }

        if (!await Reachable.Value.ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "LLM tests enabled (TORRENTBOT_RUN_LLM_TESTS=true) but Ollama is not reachable. Check LLM_HOST/LLM_PORT.");
        }

        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var adapter = new LlmPlannerAdapter(
                engine.LlmPipeline!.Planner,
                (user, scope) => engine.FilterCapabilitiesForUser(user, scope),
                () => engine.GetQuerySourceManifests(),
                engine.ConversationContextStore,
                () => engine.GetCapabilityContracts());

            var plan = await adapter.PlanAsync(
                new Invocation
                {
                    Text = scenario.Input,
                    User = AclService.FromEnvironment().ResolveUser("8153696940"),
                    RequestContext = new RequestContext(
                        Guid.NewGuid().ToString("N"),
                        Guid.NewGuid().ToString("N"),
                        "8153696940",
                        source: "llm-test",
                        chatId: $"llm-{scenario.Id}")
                },
                new PlanningContext(AclService.FromEnvironment().ResolveUser("8153696940"), IsReplay: false))
                .ConfigureAwait(false);

            LlmScenarioAsserter.AssertPlan(scenario, plan);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"[{scenario.Id}] {scenario.Input}: {ex.Message}", ex);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public void Catalog_contains_100_scenarios()
    {
        Assert.Equal(100, Scenarios.Count);
        Assert.Equal(Scenarios.Count, Scenarios.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count());
    }
}