using System.Text.Json;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public sealed class OllamaLlmResponder : ILlmResponder
{
    private readonly OllamaLlmClient _client;
    private readonly DeterministicLlmResponder _fallback = new();

    public OllamaLlmResponder(OllamaLlmClient client) => _client = client;

    public async Task<string> Compose(string userText, PlanEnvelope plan, LlmExecutionResult execution, CapabilityResult? lastResult = null)
    {
        if (!execution.Success)
        {
            return await _fallback.Compose(userText, plan, execution, lastResult).ConfigureAwait(false);
        }

        if (lastResult is { Success: true })
        {
            var resultSummary = SummarizeResult(lastResult);
            var prompt =
                "You are TorrentBot, a helpful home media and automation assistant.\n" +
                $"User originally asked: {userText}\n" +
                $"The plan intent was: {plan.Intent}\n" +
                $"Execution produced this data: {resultSummary}\n\n" +
                "Write a clear, friendly, concise reply in the same language as the user's request if possible. " +
                "Include the important data (counts, names, statuses). Use bullet points or numbered lists when there are multiple items. " +
                "Do not mention internal capabilities, plans, or JSON. Just give the user what they need.";
            var response = await _client.GenerateAsync(prompt).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(response)
                ? FormatDetailedResponse(lastResult)
                : response.Trim();
        }

        return await _fallback.Compose(userText, plan, execution, lastResult).ConfigureAwait(false);
    }

    private static string FormatDetailedResponse(CapabilityResult result)
    {
        if (result.Data is not Dictionary<string, object?> data)
        {
            return result.Message ?? "OK";
        }

        var sb = new System.Text.StringBuilder();
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
                        items.Add("  - " + string.Join(", ", parts.Take(5))); // Limit fields per item
                    }
                    else
                    {
                        items.Add($"  - {item}");
                    }
                }
                
                if (items.Count > 0)
                {
                    sb.AppendLine($"{key} ({items.Count} items):");
                    foreach (var item in items.Take(10)) // Limit to 10 items
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

    private static string SummarizeResult(CapabilityResult result)
    {
        if (result.Data is Dictionary<string, object?> data)
        {
            var summary = new Dictionary<string, object?>();
            foreach (var kvp in data)
            {
                if (kvp.Value is System.Collections.IEnumerable enumerable and not string)
                {
                    var count = 0;
                    foreach (var _ in enumerable) count++;
                    summary[kvp.Key] = $"[{count} items]";
                }
                else
                {
                    summary[kvp.Key] = kvp.Value;
                }
            }
            return JsonSerializer.Serialize(summary);
        }
        return result.Message ?? "OK";
    }
}