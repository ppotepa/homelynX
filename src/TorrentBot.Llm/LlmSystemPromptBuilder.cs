using System.Text;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Llm;

public static class LlmSystemPromptBuilder
{
    public static string BuildPlannerPrompt(LlmPlanningRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are TorrentBot, an orchestration planner for a home media and automation bot.");
        builder.AppendLine("Your job: read the user request, pick capabilities from the manifest below, and return a JSON execution plan.");
        builder.AppendLine();
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
        builder.AppendLine("2. When the user asks what commands exist, use system.help or system.capabilities.");
        builder.AppendLine("3. For torrent/content search, use torrent.search with parameters { \"query\": \"...\" }.");
        builder.AppendLine("4. For download state questions, prefer query.execute on source \"downloads\" instead of guessing.");
        builder.AppendLine("5. Multi-step plans are allowed; use save_as to name intermediate results and condition to gate steps.");
        builder.AppendLine("6. Do not invent capabilities, query source names, or parameters outside the manifest.");
        builder.AppendLine("7. If the request cannot be served, return an empty steps array and explain in intent.");
        builder.AppendLine();
        builder.AppendLine("## User request");
        builder.AppendLine(request.Text);
        builder.AppendLine();
        builder.AppendLine("Respond with JSON only (no markdown fences):");
        builder.AppendLine(
            "{\"intent\":\"short summary\",\"steps\":[{\"capability\":\"exact.name\",\"parameters\":{},\"why\":\"reason\",\"condition\":null,\"save_as\":null}],\"confidence\":0.0}");

        return builder.ToString();
    }
}