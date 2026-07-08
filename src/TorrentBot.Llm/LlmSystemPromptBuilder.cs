using System.Text;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Contracts.Repositories;
using TorrentBot.Llm.Polish;

namespace TorrentBot.Llm;

public static class LlmSystemPromptBuilder
{
    public static string BuildPlannerPrompt(LlmPlanningRequest request) =>
        BuildPlanner(request);

    public static string BuildResponseHandlingPrompt(LlmPlanningRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are TorrentBot handling a user response to a pending action.");
        AppendCapabilityContracts(builder, request);
        AppendPendingActions(builder, request.Conversation);
        AppendResponseConstruction(builder, request);
        AppendRecursionRules(builder, request);
        return builder.ToString();
    }

    private static string BuildPlanner(LlmPlanningRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are TorrentBot, an orchestration planner for a home media and automation bot.");
        builder.AppendLine("Your job: read the user request (can be in English or Polish), use conversation history + snapshots for context, pick EXACT capabilities from contracts/manifest, and return a JSON execution plan.");

        if (request.RequestNumber > 0)
        {
            builder.AppendLine($"## Conversation context");
            builder.AppendLine($"This is request #{request.RequestNumber} in the current session.");
            builder.AppendLine();
        }

        if (request.Conversation is not null)
        {
            AppendContextSnapshots(builder, request.Conversation);
            AppendConversationHistory(builder, request.Conversation);
        }

        AppendCapabilityContracts(builder, request);
        AppendPendingActions(builder, request.Conversation);
        AppendResponseConstruction(builder, request);
        AppendRecursionRules(builder, request);

        builder.AppendLine($"## Active scope: {request.Scope ?? "media"}");
        builder.AppendLine("Only use capabilities whose scope matches the active scope or is \"all\".");
        builder.AppendLine();
        builder.AppendLine("## Capability manifest");
        builder.AppendLine("Each entry lists: name, optional slash command, permission, risk, readonly flag, description, llm usage, intent hints.");
        builder.AppendLine("The field steps[].capability MUST be an exact capability name from this list — never a label, title, or query source name.");
        builder.AppendLine();

        foreach (var capability in request.Capabilities.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            builder.Append("- ").Append(capability.Name);
            if (!string.IsNullOrWhiteSpace(capability.Command))
            {
                builder.Append(" command=").Append(capability.Command);
            }

            builder.Append(" permission=").Append(capability.Permission);
            builder.Append(" risk=").Append(capability.Risk);
            if (capability.IsReadOnly)
            {
                builder.Append(" readonly");
            }

            builder.AppendLine();
            builder.Append("  description: ").AppendLine(capability.Description);
            if (!string.IsNullOrWhiteSpace(capability.LlmUsage))
            {
                builder.Append("  llm_usage: ").AppendLine(capability.LlmUsage);
            }

            if (capability.IntentHints is { Count: > 0 })
            {
                builder.Append("  intent_hints: ").AppendLine(string.Join(", ", capability.IntentHints));
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Query DSL (capability: query.execute)");
        builder.AppendLine("Use query.execute for read-only inspection of structured runtime state.");
        builder.AppendLine("Parameters:");
        builder.AppendLine("- source (required): registered query source name");
        builder.AppendLine("- where (optional): array of filters { \"field\": \"...\", \"op\": \"=\", \"value\": \"...\" }");
        builder.AppendLine("- select (optional): array of field names");
        builder.AppendLine("- limit (optional): max rows");
        builder.AppendLine("Allowed operators depend on field; common ops: =, !=, >, <, contains.");
        builder.AppendLine();

        if (request.QuerySources.Count > 0)
        {
            builder.AppendLine("### Registered query sources");
            foreach (var source in request.QuerySources.OrderBy(s => s.Name, StringComparer.Ordinal))
            {
                builder.Append("- ").Append(source.Name).Append(": ").AppendLine(source.Description);
                if (!string.IsNullOrWhiteSpace(source.LlmUsage))
                {
                    builder.Append("  llm_usage: ").AppendLine(source.LlmUsage);
                }

                builder.Append("  fields: ");
                builder.AppendLine(string.Join(", ", source.Fields.Select(f =>
                {
                    var ops = f.AllowedOperators is { Count: > 0 }
                        ? $" ops=[{string.Join(',', f.AllowedOperators)}]"
                        : string.Empty;
                    return $"{f.Name}:{f.Type}{ops}";
                })));

                if (source.ExampleQueries is { Count: > 0 })
                {
                    builder.AppendLine("  examples:");
                    foreach (var example in source.ExampleQueries)
                    {
                        builder.Append("    ").AppendLine(example);
                    }
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Planning rules");
        builder.AppendLine("1. Use ONLY capability names from the manifest (e.g. system.help, torrent.search, query.execute).");
        builder.AppendLine("2. Use capability contracts for exact semantics, user interactions, and continuations.");
        builder.AppendLine("3. Use context snapshots and pending actions to resolve follow-ups.");
        builder.AppendLine("4. Multi-step plans are allowed; use save_as to name intermediate results and condition to gate steps.");
        builder.AppendLine("5. Do not invent capabilities, query source names, or parameters outside the manifest.");
        builder.AppendLine("6. If the request cannot be served, return an empty steps array and explain in intent.");
        builder.AppendLine("7. Always provide a meaningful \"why\". Set realistic \"confidence\" (0.0-1.0).");
        builder.AppendLine();
        builder.AppendLine("## torrent.search — you choose the query");
        builder.AppendLine("- parameters.query is sent to Jackett indexers; YOU must infer a good search string from the user message.");
        builder.AppendLine("- Prefer short indexer-friendly terms (distro name + version), not the full user sentence.");
        builder.AppendLine("- Example: \"download ubuntu 22 iso\" → query \"ubuntu 22\" or \"ubuntu 22.04\", not \"download ubuntu 22 iso\".");
        builder.AppendLine("- Indexers differ; if the user is vague, pick the most likely concise query yourself.");
        builder.AppendLine();
        builder.AppendLine("## User request");
        var normalizedText = PolishLexicon.NormalizeForLlm(request.Text ?? "");
        if (!string.Equals(normalizedText, request.Text, StringComparison.Ordinal))
        {
            builder.AppendLine($"(normalized from Polish: {request.Text})");
        }
        builder.AppendLine(normalizedText);
        builder.AppendLine();

        var lexiconSample = string.Join(", ", PolishLexicon.AllWords.Take(30).Select(w => w.BaseForm));
        builder.AppendLine($"## Polish lexicon hints: {lexiconSample} ...");
        builder.AppendLine("Respond with JSON only (no markdown fences):");
        builder.AppendLine(
            "{\"intent\":\"short summary\",\"steps\":[{\"capability\":\"exact.name.from.manifest\",\"parameters\":{},\"why\":\"reason\",\"condition\":null,\"save_as\":null}],\"confidence\":0.85}");

        return builder.ToString();
    }

    private static void AppendCapabilityContracts(StringBuilder builder, LlmPlanningRequest request)
    {
        var contracts = request.Contracts ?? [];
        if (contracts.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Tool & Capability Contracts (exact semantics, interactions, continuations)");
        foreach (var contract in contracts.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            builder.Append("- ").AppendLine(contract.Name);
            builder.Append("  exact_semantics: ").AppendLine(contract.ExactSemantics);
            if (contract.Parameters is { Count: > 0 })
            {
                builder.Append("  parameters: ").AppendLine(string.Join(", ", contract.Parameters.Select(p => $"{p.Name}:{p.Type}")));
            }

            if (contract.UserInteractions is not null)
            {
                builder.Append("  user_interactions: confirm=")
                    .Append(contract.UserInteractions.RequiresConfirmation)
                    .Append(" types=")
                    .AppendLine(string.Join(",", contract.UserInteractions.ExpectedResponseTypes ?? []));
            }

            if (contract.ResponseSpec is not null)
            {
                builder.Append("  response_spec: kind=").AppendLine(contract.ResponseSpec.ArtifactKind);
            }

            if (contract.Continuations is { Count: > 0 })
            {
                foreach (var rule in contract.Continuations)
                {
                    builder.Append("  continuation: trigger=").Append(rule.Trigger)
                        .Append(" action=").Append(rule.ActionType);
                    if (rule.NextCapability is not null)
                    {
                        builder.Append(" next=").Append(rule.NextCapability);
                    }
                    builder.AppendLine();
                }
            }
        }
        builder.AppendLine();
    }

    private static void AppendPendingActions(StringBuilder builder, ConversationContext? conversation)
    {
        builder.AppendLine("## Current Conversation State & Pending Actions (N recursive)");
        if (conversation is null)
        {
            builder.AppendLine("(no conversation context)");
            builder.AppendLine();
            return;
        }

        builder.AppendLine($"session={conversation.SessionId} user={conversation.UserId} requests={conversation.RequestCount}");
        if (conversation.PendingActions.Count == 0)
        {
            builder.AppendLine("pending_actions: none");
        }
        else
        {
            foreach (var action in conversation.PendingActions)
            {
                builder.Append("- token=").Append(action.Token)
                    .Append(" cap=").Append(action.CapabilityName)
                    .Append(" expect=").Append(action.ExpectedResponse.Type)
                    .AppendLine();
            }
        }
        builder.AppendLine();
    }

    private static void AppendResponseConstruction(StringBuilder builder, LlmPlanningRequest request)
    {
        builder.AppendLine("## How to construct response (use ResponseSpec)");
        var contracts = request.Contracts ?? [];
        foreach (var contract in contracts.Where(c => c.ResponseSpec is not null))
        {
            builder.Append("- ").Append(contract.Name)
                .Append(": artifact=").Append(contract.ResponseSpec!.ArtifactKind);
            if (!string.IsNullOrWhiteSpace(contract.ResponseSpec.FormatHint))
            {
                builder.Append(" format=").Append(contract.ResponseSpec.FormatHint);
            }
            builder.AppendLine();
        }
        builder.AppendLine();
    }

    private static void AppendRecursionRules(StringBuilder builder, LlmPlanningRequest request)
    {
        builder.AppendLine("## What to expect and what to do with user response (recursion rules)");
        var contracts = request.Contracts ?? [];
        foreach (var contract in contracts.Where(c => c.Continuations is { Count: > 0 }))
        {
            foreach (var rule in contract.Continuations!)
            {
                builder.Append("- after ").Append(contract.Name)
                    .Append(" [").Append(rule.Trigger).Append("] -> ")
                    .Append(rule.ActionType);
                if (rule.ExpectedResponse is not null)
                {
                    builder.Append(" expect=").Append(rule.ExpectedResponse.Type);
                }
                builder.AppendLine();
            }
        }
        builder.AppendLine("One user response may spawn new pending actions via continuation rules.");
        builder.AppendLine();
    }

    private static void AppendContextSnapshots(StringBuilder builder, ConversationContext conversation)
    {
        if (conversation.Snapshots.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Current system state (live snapshots)");
        builder.AppendLine("Use this data to answer questions about current state without calling capabilities.");
        builder.AppendLine();

        foreach (var (source, snapshot) in conversation.Snapshots)
        {
            builder.Append("### ").AppendLine(source);

            if (source == "torrent_search_results" && snapshot.State.TryGetValue("items", out var items) && items is System.Collections.IEnumerable itemList)
            {
                builder.AppendLine("Recent search results (1-based indexes for select_result, matching display):");
                foreach (var item in itemList)
                {
                    if (item is Dictionary<string, object?> record)
                    {
                        builder.AppendLine($"  {TorrentSearchPromptFormatting.FormatLine(record)}");
                    }
                }
                builder.AppendLine();
                continue;
            }

            if (source == "downloads" && snapshot.State.TryGetValue("items", out var ditems) && ditems is System.Collections.IEnumerable dlist)
            {
                builder.AppendLine("Current downloads:");
                int idx = 0;
                foreach (var item in dlist)
                {
                    builder.AppendLine($"  [{idx}] {item}");
                    idx++;
                }
                builder.AppendLine();
                continue;
            }

            foreach (var (key, value) in snapshot.State)
            {
                if (value is null)
                {
                    continue;
                }

                builder.Append("- ").Append(key).Append(": ").AppendLine(value.ToString());
            }
            builder.AppendLine();
        }
    }

    private static void AppendConversationHistory(StringBuilder builder, ConversationContext conversation)
    {
        if (conversation.History.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Recent conversation");
        foreach (var message in conversation.History.TakeLast(6))
        {
            builder.Append("[").Append(message.Role).Append(" #").Append(message.RequestNumber).Append("] ").AppendLine(message.Content);
        }
        builder.AppendLine();
    }
}