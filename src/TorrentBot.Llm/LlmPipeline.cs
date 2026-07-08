using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Conversation;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Llm;
using TorrentBot.Contracts.Audit;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Repositories;
using TorrentBot.Llm.Polish;

namespace TorrentBot.Llm;

public sealed record LlmPipelineRequest(
    string Text,
    IReadOnlyList<CapabilityMetadata> Capabilities,
    IReadOnlyList<QuerySourceMeta>? QuerySourceManifests = null,
    bool IsDryRun = false,
    string? Scope = "media",
    IRequestContext? AuditContext = null,
    ConversationContext? Conversation = null,
    int RequestNumber = 0,
    IProgressReporter? ProgressReporter = null,
    IReadOnlyList<CapabilityContract>? Contracts = null);

public sealed record LlmPipelineResult(
    PlanEnvelope Plan,
    LlmExecutionResult Execution,
    string Reply);

public sealed class LlmPipeline
{
    private readonly ILlmPlanner _planner;

    public ILlmPlanner Planner => _planner;
    private readonly ILlmExecutor _executor;
    private readonly ILlmResponder _responder;
    private readonly IAuditSink? _auditSink;

    public LlmPipeline(
        ILlmPlanner planner,
        ILlmExecutor executor,
        ILlmResponder? responder = null,
        IAuditSink? auditSink = null)
    {
        _planner = planner;
        _executor = executor;
        _responder = responder ?? new DeterministicLlmResponder();
        _auditSink = auditSink;
    }

    public async Task<LlmPipelineResult> RunAsync(LlmPipelineRequest request, CancellationToken ct = default)
    {
        // Normalize Polish input early using lexicon (helps small models with common command words)
        var text = request.Text ?? string.Empty;
        var normalizedText = Polish.PolishLexicon.NormalizeForLlm(text);

        if (request.ProgressReporter is not null && !string.Equals(normalizedText, text, StringComparison.Ordinal))
        {
            request.ProgressReporter.Report("debug:llm:normalized", $"\"{text}\" → \"{normalizedText}\"");
        }

        // Keyword lists for extensibility (table-driven style per plan)
        var searchKeywords = new[] { "znajdź", "szukaj", "search", "find " };
        var statusListKeywords = new[] {
            "pokaż pobierania", "pokaz pobierania", "pokaż status", "pokaz status",
            "status pobierania", "stan pobierania", "pokaż torrenty", "pokaz torrenty",
            "status torrenty", "stan torrentow", "list torrents", "pokaż torrenty",
            "co się pobiera", "co pobiera", "pobierania", "torrenty status"
        };

        // Pre-process for search intent to force correct cap for small models
        var lower = normalizedText.ToLowerInvariant();
        string? forcedSearchQuery = null;
        if (searchKeywords.Any(k => lower.Contains(k)) 
            && !lower.Contains("status") && !lower.Contains("pokaż") && !lower.Contains("show") && !lower.Contains("pokaż status"))
        {
            // Extract query terms heuristically
            var q = text;
            if (lower.Contains("znajdź ")) q = text.Substring(text.ToLower().IndexOf("znajdź ") + 7).Trim();
            else if (lower.Contains("szukaj ")) q = text.Substring(text.ToLower().IndexOf("szukaj ") + 6).Trim();
            else if (lower.Contains("search for ")) q = text.Substring(text.ToLower().IndexOf("search for ") + 11).Trim();
            else if (lower.Contains("find ")) q = text.Substring(text.ToLower().IndexOf("find ") + 5).Trim();
            else if (lower.Contains("search ")) q = text.Substring(text.ToLower().IndexOf("search ") + 7).Trim();
            normalizedText = $"search for {q} -- MUST use torrent.search capability with this query";
            forcedSearchQuery = q;
        }

        // Pre-process for status/list queries (table-driven keywords) to force rich list capabilities
        bool isStatusList = statusListKeywords.Any(k => lower.Contains(k)) ||
                            (lower.Contains("pokaż") && (lower.Contains("pobrani") || lower.Contains("torrent") || lower.Contains("download") || lower.Contains("pobier")));
        if (isStatusList && !searchKeywords.Any(k => lower.Contains(k)))
        {
            if (lower.Contains("torrent") || lower.Contains("torrenty") || lower.Contains("torrents"))
            {
                normalizedText = "list torrents -- MUST use torrent.list capability for rich status";
            }
            else
            {
                normalizedText = "show downloads -- MUST use download.list or query.execute source=downloads for rich details (progress, speed, eta)";
            }
        }

        var scopedCapabilities = request.Capabilities
            .Where(c => string.Equals(c.Scope, request.Scope ?? "media", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Scope, "all", StringComparison.OrdinalIgnoreCase))
            .ToList();

        request.ProgressReporter?.Report("debug:phase:start", "llm_prompt_build");
        request.ProgressReporter?.Report("debug:llm:start", $"normalized_len={normalizedText.Length} caps={scopedCapabilities.Count}");

        var queryManifests = request.QuerySourceManifests ?? [];
        request.ProgressReporter?.Report("debug:phase:end", "llm_prompt_build");
        request.ProgressReporter?.Report("debug:phase:start", "llm_call");
        var plan = await _planner.PlanAsync(
            new LlmPlanningRequest(
                normalizedText,
                scopedCapabilities,
                queryManifests,
                request.Scope,
                request.Conversation,
                request.RequestNumber,
                request.ProgressReporter,
                request.Contracts),
            ct).ConfigureAwait(false);

        // Post-plan repair for weak small models: if search intent words present but planner chose query.execute on non-state source OR returned empty steps, force torrent.search.
        // This makes the pure NL/LM path succeed more often for the critical first step of multi-stage scenarios (T011, T054 etc) even when 1.5b model ignores CRITICAL rules.
        if (forcedSearchQuery != null && (plan.Steps.Count == 0 ||
            (plan.Steps.Count > 0 && string.Equals(plan.Steps[0].Capability, "query.execute", StringComparison.OrdinalIgnoreCase)
             && !string.Equals( (plan.Steps[0].Parameters != null && plan.Steps[0].Parameters.TryGetValue("source", out var srcVal) ? srcVal?.ToString() : null) , "downloads", StringComparison.OrdinalIgnoreCase)
             && !string.Equals( (plan.Steps[0].Parameters != null && plan.Steps[0].Parameters.TryGetValue("source", out var srcVal2) ? srcVal2?.ToString() : null) , "jobs", StringComparison.OrdinalIgnoreCase))))
        {
            var correctedSteps = new List<PlanStep>
            {
                new PlanStep(
                    Capability: "torrent.search",
                    Parameters: new Dictionary<string, object?> { ["query"] = forcedSearchQuery },
                    Why: "Corrected by pipeline repair: user requested search (znajdź/szukaj/search/find); LLM returned empty or misrouted plan. Forcing torrent.search to enable multi-stage flows.",
                    SaveAs: "search_results")
            };
            plan = new PlanEnvelope(
                Intent: (plan.Intent ?? "search") + " [search-corrected-by-pipeline]",
                Steps: correctedSteps,
                Confidence: Math.Max(0.6, plan.Confidence),
                ReplyMode: plan.ReplyMode,
                Notes: "Auto-corrected search intent for LM path robustness");

            request.ProgressReporter?.Report("debug:llm:repair", $"forced torrent.search for query='{forcedSearchQuery}'");
        }

        // Follow-up repair for multi-stage (select/pause/resume/start after search): helps weak model on T021+ T054+ etc.
        // MUCH more aggressive: if the utterance is clearly a follow-up keyword, override (stronger when context present per plan).
        var followLower = normalizedText.ToLowerInvariant();
        bool looksLikeFollowUp = followLower.Contains("wybierz") || followLower.Contains("select") || followLower.Contains("pierwszy") ||
                                 followLower.Contains("pobierz") || followLower.Contains("zacznij") || followLower.Contains("start") ||
                                 followLower.Contains("pauzuj") || followLower.Contains("pause") ||
                                 followLower.Contains("wznów") || followLower.Contains("resume") ||
                                 followLower.Contains("pokaż") || followLower.Contains("show") || followLower.Contains("status") ||
                                 followLower.Contains("pokaz") || followLower.Contains("pokaż status") || followLower.Contains("pokaz status") ||
                                 followLower.Contains("lista") || followLower.Contains("list") || followLower.Contains("pobierania");

        bool hasFollowContext = request.Conversation != null &&
            (request.Conversation.Snapshots.Any(s => s.Key.Contains("search") || s.Key.Contains("download") || s.Key.Contains("torrent")) ||
             request.Conversation.History.Count > 0 || request.RequestNumber > 0);

        if (hasFollowContext && request.ProgressReporter is not null)
        {
            request.ProgressReporter.Report("debug:context:followup", "follow-up context detected (snapshots/history present)");
        }

        bool currentPlanIsSensibleFollowUp = plan.Steps.Count > 0 &&
            (plan.Steps[0].Capability.StartsWith("torrent.") ||
             plan.Steps[0].Capability.StartsWith("download.") ||
             (plan.Steps[0].Capability == "query.execute" &&
              plan.Steps[0].Parameters != null &&
              plan.Steps[0].Parameters.TryGetValue("source", out var src) &&
              (src?.ToString()?.Equals("downloads", StringComparison.OrdinalIgnoreCase) == true ||
               src?.ToString()?.Equals("jobs", StringComparison.OrdinalIgnoreCase) == true)));

        // Always force follow-up when keywords match or context suggests follow-up (small model unreliable; stronger forcing after context per plan).
        // This makes practical pure-NL multi-stage scenarios work.
        if (looksLikeFollowUp || (hasFollowContext && (followLower.Contains("pokaż") || followLower.Contains("status") || followLower.Contains("pobierania") || followLower.Contains("torrenty"))))
        {
            string forcedCap = "";
            Dictionary<string, object?>? parms = null;
            string why = "Aggressive follow-up repair (context + keywords)";
            string save = null;

            if (IndexSelectionParsing.LooksLikeIndexSelection(normalizedText))
            {
                forcedCap = "torrent.select_result";
                if (IndexSelectionParsing.TryParseDisplayIndex(normalizedText, out var displayIndex))
                {
                    parms = new Dictionary<string, object?> { ["index"] = displayIndex };
                    why = $"Follow-up repair: parsed 1-based display index {displayIndex} from utterance";
                }
                else
                {
                    parms = new Dictionary<string, object?> { ["index"] = 1 };
                    why = "Follow-up repair: index selection keyword without number -> default index 1";
                }

                save = "selected";
            }
            else if (followLower.Contains("pauzuj") || followLower.Contains("pause"))
            {
                forcedCap = "download.pause";
                parms = new Dictionary<string, object?>();
                why = "Follow-up repair: pauzuj/pause using downloads context";
            }
            else if (followLower.Contains("wznów") || followLower.Contains("resume"))
            {
                forcedCap = "download.resume";
                parms = new Dictionary<string, object?>();
                why = "Follow-up repair: wznów/resume the download";
            }
            else if (followLower.Contains("pobierz") || followLower.Contains("zacznij") || followLower.Contains("start"))
            {
                forcedCap = "download.start";
                parms = new Dictionary<string, object?>();
                why = "Follow-up repair: start the selected/previous download";
            }
            else if (followLower.Contains("pokaż") || followLower.Contains("status") || followLower.Contains("query"))
            {
                if (followLower.Contains("torrent") || followLower.Contains("torrenty"))
                {
                    forcedCap = "torrent.list";
                    parms = new Dictionary<string, object?>();
                    why = "Follow-up repair: show rich torrent status using torrent.list (progress, speeds, state)";
                }
                else
                {
                    forcedCap = "query.execute";
                    parms = new Dictionary<string, object?> { ["source"] = "downloads" };
                    why = "Follow-up repair: show rich download status using query (now includes progress, dlspeed, eta)";
                }
            }

            if (!string.IsNullOrEmpty(forcedCap))
            {
                var fsteps = new List<PlanStep> { new PlanStep(forcedCap, parms, why, SaveAs: save) };
                plan = new PlanEnvelope(
                    Intent: (plan.Intent ?? "follow-up") + " [followup-corrected-aggressive]",
                    Steps: fsteps,
                    Confidence: 0.7,
                    ReplyMode: plan.ReplyMode,
                    Notes: "Aggressive auto-corrected follow-up for multi-turn LM path");

                request.ProgressReporter?.Report("debug:llm:repair", $"forced {forcedCap} (follow-up keyword + context)");
            }
        }

        if (_auditSink is not null && request.AuditContext is not null)
        {
            _auditSink.Write(
                "natural_plan",
                request.AuditContext,
                "llm",
                plan.Steps.Count > 0,
                JsonSerializer.Serialize(new { intent = plan.Intent, steps = plan.Steps.Count, scope = request.Scope }));
        }

        if (_executor is AuditingLlmExecutor auditing && request.AuditContext is not null)
        {
            auditing.SetAuditContext(request.AuditContext);
        }

        request.ProgressReporter?.Report("debug:phase:end", "llm_call");
        request.ProgressReporter?.Report("debug:phase:start", "execution");
        var execution = await _executor.Execute(new LlmExecutionRequest(plan, scopedCapabilities, request.IsDryRun)).ConfigureAwait(false);

        if (_auditSink is not null && request.AuditContext is not null)
        {
            _auditSink.Write(
                "natural_execution",
                request.AuditContext,
                "llm",
                execution.Success,
                JsonSerializer.Serialize(new { success = execution.Success, stepsToExecute = execution.StepsToExecute.Count }));
        }

        request.ProgressReporter?.Report("debug:phase:end", "execution");
        return new LlmPipelineResult(plan, execution, string.Empty);
    }

    public async Task<string> ComposeReply(string userText, PlanEnvelope plan, LlmExecutionResult execution, CapabilityResult? lastResult = null)
    {
        var reply = await _responder.Compose(userText, plan, execution, lastResult).ConfigureAwait(false);

        if (_auditSink is not null && execution.StepsToExecute.Count > 0)
        {
            _auditSink.Write(
                "natural_response",
                new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "system", source: "llm"),
                "llm",
                execution.Success,
                JsonSerializer.Serialize(new { reply, success = execution.Success }));
        }

        return reply;
    }
}