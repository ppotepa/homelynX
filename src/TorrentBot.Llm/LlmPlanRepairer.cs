using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Conversation;
using TorrentBot.Contracts.Llm;
using TorrentBot.Contracts.Pipeline;

namespace TorrentBot.Llm;

public static class LlmPlanRepairer
{
    public static PlanEnvelope Repair(
        LlmIntentContext intent,
        PlanEnvelope plan,
        ConversationContext? conversation = null,
        int requestNumber = 0,
        IProgressReporter? progress = null)
    {
        plan = RepairSearchIntent(intent, plan, progress);
        plan = RepairFollowUp(intent, plan, conversation, requestNumber, progress);
        return plan;
    }

    private static PlanEnvelope RepairSearchIntent(
        LlmIntentContext intent,
        PlanEnvelope plan,
        IProgressReporter? progress)
    {
        var forcedQuery = intent.ForcedSearchQuery;
        if (forcedQuery is null)
        {
            if (plan.Steps.Count == 0
                && LlmIntentNormalizer.TryExtractDownloadSearchQuery(intent.OriginalText, out var fallbackQuery))
            {
                forcedQuery = fallbackQuery;
            }
            else
            {
                return plan;
            }
        }

        var misrouted = plan.Steps.Count > 0
            && string.Equals(plan.Steps[0].Capability, "query.execute", StringComparison.OrdinalIgnoreCase)
            && plan.Steps[0].Parameters?.TryGetValue("source", out var src) == true
            && !string.Equals(src?.ToString(), "downloads", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(src?.ToString(), "jobs", StringComparison.OrdinalIgnoreCase);

        if (plan.Steps.Count > 0 && !misrouted)
        {
            return plan;
        }

        progress?.Report("debug:llm:repair", $"forced torrent.search for query='{forcedQuery}'");

        return new PlanEnvelope(
            Intent: (plan.Intent ?? "search") + " [search-corrected-by-pipeline]",
            Steps:
            [
                new PlanStep(
                    Capability: "torrent.search",
                    Parameters: new Dictionary<string, object?> { ["query"] = forcedQuery },
                    Why: "Corrected by pipeline repair: user requested search/download-by-title; LLM returned empty or misrouted plan.",
                    SaveAs: "search_results")
            ],
            Confidence: Math.Max(0.6, plan.Confidence),
            ReplyMode: plan.ReplyMode,
            Notes: "Auto-corrected search intent for LM path robustness");
    }

    private static PlanEnvelope RepairFollowUp(
        LlmIntentContext intent,
        PlanEnvelope plan,
        ConversationContext? conversation,
        int requestNumber,
        IProgressReporter? progress)
    {
        var followLower = intent.NormalizedText.ToLowerInvariant();
        var looksLikeFollowUp = followLower.Contains("wybierz")
            || followLower.Contains("select")
            || followLower.Contains("pierwszy")
            || followLower.Contains("pauzuj")
            || followLower.Contains("pause")
            || followLower.Contains("wznów")
            || followLower.Contains("resume")
            || followLower.Contains("pokaż")
            || followLower.Contains("pokaz")
            || followLower.Contains("show")
            || followLower.Contains("status")
            || followLower.Contains("lista")
            || followLower.Contains("list")
            || followLower.Contains("pobierania")
            || (followLower.Contains("pobierz") && !LlmIntentNormalizer.TryExtractDownloadSearchQuery(intent.OriginalText, out _))
            || ((followLower.Contains("zacznij") || followLower.Contains("start")) && HasFollowContext(conversation, requestNumber));

        var hasFollowContext = HasFollowContext(conversation, requestNumber);
        if (!looksLikeFollowUp && !(hasFollowContext && (followLower.Contains("pokaż") || followLower.Contains("status") || followLower.Contains("pobierania") || followLower.Contains("torrenty"))))
        {
            return plan;
        }

        if (hasFollowContext)
        {
            progress?.Report("debug:context:followup", "follow-up context detected (snapshots/history present)");
        }

        string? forcedCap = null;
        Dictionary<string, object?>? parms = null;
        string why = "Aggressive follow-up repair (context + keywords)";
        string? save = null;

        if (IndexSelectionParsing.LooksLikeIndexSelection(intent.NormalizedText))
        {
            forcedCap = "torrent.select_result";
            if (IndexSelectionParsing.TryParseDisplayIndex(intent.NormalizedText, out var displayIndex))
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
        else if (hasFollowContext && (followLower.Contains("pobierz") || followLower.Contains("zacznij") || followLower.Contains("start")))
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
                why = "Follow-up repair: show rich torrent status using torrent.list";
            }
            else
            {
                forcedCap = "query.execute";
                parms = new Dictionary<string, object?> { ["source"] = "downloads" };
                why = "Follow-up repair: show rich download status using query";
            }
        }

        if (string.IsNullOrEmpty(forcedCap))
        {
            return plan;
        }

        progress?.Report("debug:llm:repair", $"forced {forcedCap} (follow-up keyword + context)");

        return new PlanEnvelope(
            Intent: (plan.Intent ?? "follow-up") + " [followup-corrected-aggressive]",
            Steps: [new PlanStep(forcedCap, parms, why, SaveAs: save)],
            Confidence: 0.7,
            ReplyMode: plan.ReplyMode,
            Notes: "Aggressive auto-corrected follow-up for multi-turn LM path");
    }

    private static bool HasFollowContext(ConversationContext? conversation, int requestNumber) =>
        conversation is not null
        && (conversation.Snapshots.Any(s => s.Key.Contains("search", StringComparison.Ordinal)
            || s.Key.Contains("download", StringComparison.Ordinal)
            || s.Key.Contains("torrent", StringComparison.Ordinal))
            || conversation.History.Count > 0
            || requestNumber > 0);
}