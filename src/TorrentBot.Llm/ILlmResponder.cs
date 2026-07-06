using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public interface ILlmResponder
{
    string Compose(string originalText, PlanEnvelope plan, LlmExecutionResult executionResult);
}