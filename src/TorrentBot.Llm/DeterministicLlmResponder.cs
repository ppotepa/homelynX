using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public sealed class DeterministicLlmResponder : ILlmResponder
{
    public string Compose(string originalText, PlanEnvelope plan, LlmExecutionResult executionResult)
    {
        if (!executionResult.Success)
        {
            return executionResult.Error ?? "Plan execution failed.";
        }

        if (plan.Steps.Count == 0)
        {
            return "I could not derive a plan for that request.";
        }

        var steps = string.Join(", ", plan.Steps.Select(s => s.Capability));
        return $"Planned {plan.Intent} with step(s): {steps}.";
    }
}