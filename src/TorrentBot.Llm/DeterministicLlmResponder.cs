using System.Text;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public sealed class DeterministicLlmResponder : ILlmResponder
{
    public Task<string> Compose(string originalText, PlanEnvelope plan, LlmExecutionResult executionResult, CapabilityResult? lastResult = null)
    {
        if (!executionResult.Success)
        {
            return Task.FromResult(executionResult.Error ?? "Plan execution failed.");
        }

        if (plan.Steps.Count == 0)
        {
            return Task.FromResult("I could not derive a plan for that request.");
        }

        if (lastResult is { Success: true })
        {
            return Task.FromResult(FormatDetailedResponse(lastResult));
        }

        var steps = string.Join(", ", plan.Steps.Select(s => s.Capability));
        return Task.FromResult($"Planned {plan.Intent} with step(s): {steps}.");
    }

    private static string FormatDetailedResponse(CapabilityResult result)
    {
        if (result.Data is not Dictionary<string, object?> data)
        {
            return result.Message ?? "OK";
        }

        var sb = new StringBuilder();
        sb.AppendLine(result.Message ?? "Result:");
        sb.AppendLine();

        foreach (var (key, value) in data)
        {
            if (value is null) continue;

            if (value is System.Collections.IEnumerable enumerable and not string)
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                {
                    if (item is Dictionary<string, object?> dict)
                    {
                        var parts = new List<string>();
                        foreach (var (k, v) in dict)
                        {
                            if (v is not null)
                            {
                                parts.Add($"{k}: {v}");
                            }
                        }
                        items.Add("  - " + string.Join(", ", parts.Take(5)));
                    }
                    else
                    {
                        items.Add($"  - {item}");
                    }
                }
                
                if (items.Count > 0)
                {
                    sb.AppendLine($"{key} ({items.Count} items):");
                    foreach (var item in items.Take(10))
                    {
                        sb.AppendLine(item);
                    }
                    if (items.Count > 10)
                    {
                        sb.AppendLine($"  ... and {items.Count - 10} more");
                    }
                }
            }
            else
            {
                sb.AppendLine($"{key}: {value}");
            }
        }

        return sb.ToString().Trim();
    }
}