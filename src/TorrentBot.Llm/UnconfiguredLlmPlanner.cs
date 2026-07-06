using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public sealed class UnconfiguredLlmPlanner : ILlmPlanner
{
    public Task<PlanEnvelope> PlanAsync(LlmPlanningRequest request, CancellationToken ct = default) =>
        Task.FromResult(new PlanEnvelope(
            Intent: "llm_unavailable",
            Steps: [],
            Confidence: 0,
            Notes: "Natural-language planning requires an LLM endpoint. Set OLLAMA_HOST or TORRENTBOT_OLLAMA_URL."));
}