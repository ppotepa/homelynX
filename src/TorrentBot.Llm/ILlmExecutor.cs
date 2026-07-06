using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public sealed record LlmExecutionRequest(
    PlanEnvelope Plan,
    IReadOnlyList<CapabilityMetadata> Capabilities,
    bool IsDryRun = false);

public sealed record LlmStepResult(
    PlanStep Step,
    bool Success,
    string? Error = null,
    bool Skipped = false);

public sealed record LlmExecutionResult(
    bool Success,
    IReadOnlyList<LlmStepResult> StepResults,
    IReadOnlyList<PlanStep> StepsToExecute,
    string? Error = null);

public interface ILlmExecutor
{
    LlmExecutionResult Execute(LlmExecutionRequest request);
}