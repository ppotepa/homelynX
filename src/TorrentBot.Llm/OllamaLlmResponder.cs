using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public sealed class OllamaLlmResponder : ILlmResponder
{
    private readonly OllamaLlmClient _client;
    private readonly DeterministicLlmResponder _fallback = new();

    public OllamaLlmResponder(OllamaLlmClient client) => _client = client;

    public string Compose(string userText, PlanEnvelope plan, LlmExecutionResult execution)
    {
        if (!execution.Success)
        {
            return _fallback.Compose(userText, plan, execution);
        }

        var prompt = $"User asked: {userText}\nCompose a concise helpful reply based on plan intent '{plan.Intent}'.";
        var response = _client.GenerateAsync(prompt).GetAwaiter().GetResult();
        return string.IsNullOrWhiteSpace(response)
            ? _fallback.Compose(userText, plan, execution)
            : response.Trim();
    }
}