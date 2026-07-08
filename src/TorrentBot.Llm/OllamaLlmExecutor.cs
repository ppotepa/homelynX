using System.Text.Json;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public sealed class OllamaLlmExecutor : ILlmExecutor
{
    private readonly OllamaLlmClient _client;
    private readonly StubLlmExecutor _fallback;

    public OllamaLlmExecutor(OllamaLlmClient client, StubLlmExecutor? fallback = null)
    {
        _client = client;
        _fallback = fallback ?? new StubLlmExecutor();
    }

    public async Task<LlmExecutionResult> Execute(LlmExecutionRequest request)
    {
        var fallback = await _fallback.Execute(request).ConfigureAwait(false);
        if (!fallback.Success || request.IsDryRun)
        {
            return fallback;
        }

        var prompt =
            "You are a strict plan validator for a home automation bot.\n" +
            "The plan below was produced by an LLM planner using the known capability manifest.\n" +
            "Check if every step.capability is a real registered capability and parameters look plausible.\n" +
            "Respond ONLY with JSON: {\"approved\": true} or {\"approved\": false, \"error\": \"short reason\"}.\n\n" +
            JsonSerializer.Serialize(request.Plan);

        var response = await _client.GenerateAsync(prompt).ConfigureAwait(false);
        if (TryParseApproval(response, out var approved, out var error) && !approved)
        {
            return new LlmExecutionResult(
                Success: false,
                StepResults: fallback.StepResults,
                StepsToExecute: fallback.StepsToExecute,
                Error: error ?? "Executor rejected plan.");
        }

        return fallback;
    }

    private static bool TryParseApproval(string response, out bool approved, out string? error)
    {
        approved = true;
        error = null;
        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        try
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return false;
            }

            using var document = JsonDocument.Parse(response[start..(end + 1)]);
            if (document.RootElement.TryGetProperty("approved", out var approvedElement))
            {
                approved = approvedElement.GetBoolean();
            }

            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                error = errorElement.GetString();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}