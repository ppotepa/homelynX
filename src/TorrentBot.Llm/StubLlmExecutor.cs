using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public sealed class StubLlmExecutor : ILlmExecutor
{
    public LlmExecutionResult Execute(LlmExecutionRequest request)
    {
        var knownCapabilities = request.Capabilities
            .Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);

        var stepResults = new List<LlmStepResult>();
        var stepsToExecute = new List<PlanStep>();
        var saved = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var step in request.Plan.Steps)
        {
            if (!PlanStepConditionEvaluator.ShouldExecute(step.Condition, saved))
            {
                stepResults.Add(new LlmStepResult(step, Success: true, Error: "condition_not_met", Skipped: true));
                continue;
            }

            if (!knownCapabilities.Contains(step.Capability))
            {
                stepResults.Add(new LlmStepResult(step, Success: false, Error: $"Capability '{step.Capability}' is not registered."));
                return new LlmExecutionResult(
                    Success: false,
                    StepResults: stepResults,
                    StepsToExecute: stepsToExecute,
                    Error: $"Unknown capability '{step.Capability}'.");
            }

            if (step.ConfirmationToken is not null && !request.IsDryRun)
            {
                stepResults.Add(new LlmStepResult(step, Success: false, Error: "Confirmation is required."));
                return new LlmExecutionResult(
                    Success: false,
                    StepResults: stepResults,
                    StepsToExecute: stepsToExecute,
                    Error: "Plan step requires confirmation.");
            }

            stepResults.Add(new LlmStepResult(step, Success: true));
            stepsToExecute.Add(step);

            if (!string.IsNullOrWhiteSpace(step.SaveAs))
            {
                saved[step.SaveAs] = new Dictionary<string, object?> { ["capability"] = step.Capability, ["validated"] = true };
            }
        }

        return new LlmExecutionResult(Success: true, StepResults: stepResults, StepsToExecute: stepsToExecute);
    }
}