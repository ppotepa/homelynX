using TorrentBot.Contracts.Llm;
using TorrentBot.Llm;

namespace TorrentBot.Engine.Tests.Support;

internal sealed class FixedPlanLlmPlanner : ILlmPlanner
{
    private readonly Func<LlmPlanningRequest, PlanEnvelope> _planFactory;

    public FixedPlanLlmPlanner(Func<LlmPlanningRequest, PlanEnvelope> planFactory) =>
        _planFactory = planFactory;

    public Task<PlanEnvelope> PlanAsync(LlmPlanningRequest request, CancellationToken ct = default) =>
        Task.FromResult(_planFactory(request));

    public static FixedPlanLlmPlanner HealthCheck() =>
        new(_ => new PlanEnvelope(
            Intent: "check system health",
            Steps: [new PlanStep("system.health", Why: "Fixed test planner health probe")],
            Confidence: 1,
            Notes: "Fixed test planner"));

    public static FixedPlanLlmPlanner ActiveDownloads() =>
        new(request => new PlanEnvelope(
            Intent: "inspect active downloads",
            Steps:
            [
                new PlanStep(
                    "query.execute",
                    new Dictionary<string, object?>
                    {
                        ["source"] = "downloads",
                        ["where"] = new[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["field"] = "status",
                                ["op"] = "=",
                                ["value"] = "downloading"
                            }
                        }
                    },
                    Why: $"Planned from test prompt: {request.Text}")
            ],
            Confidence: 1,
            Notes: "Fixed test planner"));
}