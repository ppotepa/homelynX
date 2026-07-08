using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public interface ILlmResponder
{
    Task<string> Compose(string originalText, PlanEnvelope plan, LlmExecutionResult executionResult, CapabilityResult? lastResult = null);
}