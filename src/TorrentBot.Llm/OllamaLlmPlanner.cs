using System.Collections.Generic;
using System.Diagnostics;
using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

public sealed class OllamaLlmPlanner : ILlmPlanner
{
    private readonly OllamaLlmClient _client;
    private readonly LlmAuditLogger? _auditLogger;
    private const int MaxRetries = 2;

    public OllamaLlmPlanner(OllamaLlmClient client, LlmAuditLogger? auditLogger = null)
    {
        _client = client;
        _auditLogger = auditLogger;
    }

    public async Task<PlanEnvelope> PlanAsync(LlmPlanningRequest request, CancellationToken ct = default)
    {
        var progress = request.ProgressReporter;
        var sw = Stopwatch.StartNew();
        var prompt = LlmSystemPromptBuilder.BuildPlannerPrompt(request);

        var ctxInfo = request.Conversation != null 
            ? $"ctx:hist={request.Conversation.History.Count} snaps={request.Conversation.Snapshots.Count}" 
            : "ctx:none";
        _auditLogger?.LogPrompt("planner", prompt, request.Capabilities.Count, request.Scope);
        _auditLogger?.LogFullPrompt("planner", prompt, request.Text);
        progress?.Report("debug:llm:prompt", $"{prompt.Length}|{request.Capabilities.Count}|{request.Scope}|{ctxInfo}");

        if (request.ProgressReporter is not null)
        {
            progress.Report("debug:llm:full_prompt", prompt);
        }

        var response = await _client.GenerateAsync(prompt, ct).ConfigureAwait(false);
        sw.Stop();

        _auditLogger?.LogResponse("planner", response, sw.ElapsedMilliseconds);

        if (request.ProgressReporter is not null && !string.IsNullOrWhiteSpace(response))
        {
            request.ProgressReporter.Report("debug:llm:raw_response", response);
        }
        progress?.Report("debug:llm:response", $"{sw.ElapsedMilliseconds}|{response?.Length ?? 0}|{(string.IsNullOrEmpty(response) ? "empty" : "ok")}");

        if (LlmPlanParser.TryParse(response, request, out var plan))
        {
            plan = EnrichPlanWithFilterIfNeeded(plan, request.Text);
            _auditLogger?.LogPlan("planner", plan.Intent, plan.Steps.Count, plan.Confidence);
            _auditLogger?.LogFullPrompt("planner_parsed_plan", System.Text.Json.JsonSerializer.Serialize(new { plan.Intent, plan.Steps, plan.Confidence }), request.Text);
            return plan;
        }

        progress?.Report("debug:llm:parse_failed", null);
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            progress?.Report("debug:llm:repair", $"{attempt}/{MaxRetries}");

            var repairPrompt = BuildRepairPrompt(prompt, response, attempt);
            sw.Restart();
            response = await _client.GenerateAsync(repairPrompt, ct).ConfigureAwait(false);
            sw.Stop();

            _auditLogger?.LogResponse("planner", response, sw.ElapsedMilliseconds, $"repair:{attempt}");
            _auditLogger?.LogFullPrompt("planner_repair", repairPrompt, $"attempt{attempt}");
            progress?.Report("debug:llm:response", $"{sw.ElapsedMilliseconds}|{response?.Length ?? 0}|repair:{attempt}");

            if (LlmPlanParser.TryParse(response, request, out plan))
            {
                plan = EnrichPlanWithFilterIfNeeded(plan, request.Text);
                _auditLogger?.LogPlan("planner", plan.Intent, plan.Steps.Count, plan.Confidence);
                return plan;
            }
        }

        return PlanEnvelopeFactory.Unsupported("LLM response could not be parsed into a valid execution plan after retries.");
    }

    private static string BuildRepairPrompt(string originalPrompt, string invalidResponse, int attempt)
    {
        return originalPrompt + "\n\n---\n" +
            "Your previous response was invalid JSON:\n" +
            invalidResponse + "\n\n" +
            "Please respond with valid JSON only (no markdown fences, no explanation):\n" +
            "{\"intent\":\"short summary\",\"steps\":[{\"capability\":\"exact.name\",\"parameters\":{},\"why\":\"reason\",\"condition\":null,\"save_as\":null}],\"confidence\":0.0}\n\n" +
            "This is retry attempt " + attempt + ". Ensure the response is parseable JSON.";
    }

    private static PlanEnvelope EnrichPlanWithFilterIfNeeded(PlanEnvelope plan, string userText)
    {
        if (string.IsNullOrWhiteSpace(userText) || plan.Steps.Count == 0)
            return plan;

        var textLower = userText.ToLowerInvariant();
        string? inferredFilter = null;
        if (textLower.Contains("pob") || textLower.Contains("download") || textLower.Contains("ściąg"))
            inferredFilter = "download";
        else if (textLower.Contains("torrent") || textLower.Contains("torent"))
            inferredFilter = "torrent";
        else if (textLower.Contains("job"))
            inferredFilter = "job";

        if (inferredFilter == null)
            return plan;

        var newSteps = new List<PlanStep>();
        bool changed = false;
        foreach (var step in plan.Steps)
        {
            if ((step.Capability == "system.help" || step.Capability == "system.capabilities")
                && (step.Parameters == null || !step.Parameters.ContainsKey("filter")))
            {
                var newParams = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (step.Parameters != null)
                {
                    foreach (var kv in step.Parameters) newParams[kv.Key] = kv.Value;
                }
                newParams["filter"] = inferredFilter;
                newSteps.Add(step with { Parameters = newParams });
                changed = true;
            }
            else
            {
                newSteps.Add(step);
            }
        }

        if (!changed)
            return plan;

        return plan with { Steps = newSteps };
    }
}