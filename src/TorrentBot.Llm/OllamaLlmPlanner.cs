using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public sealed class OllamaLlmPlanner : ILlmPlanner
{
    private readonly OllamaLlmClient _client;

    public OllamaLlmPlanner(OllamaLlmClient client) => _client = client;

    public async Task<PlanEnvelope> PlanAsync(LlmPlanningRequest request, CancellationToken ct = default)
    {
        var prompt = LlmSystemPromptBuilder.BuildPlannerPrompt(request);
        var response = await _client.GenerateAsync(prompt, ct).ConfigureAwait(false);
        if (!LlmPlanParser.TryParse(response, request, out var plan))
        {
            return PlanEnvelopeFactory.Unsupported("LLM response could not be parsed into a valid execution plan.");
        }

        return plan;
    }
}