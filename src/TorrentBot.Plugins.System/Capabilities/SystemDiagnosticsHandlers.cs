using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Llm;

namespace TorrentBot.Plugins.System.Capabilities;

public sealed class SystemHelpHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var filter = GetString(parameters, "filter")
                     ?? GetString(parameters, "category")
                     ?? GetString(parameters, "module")
                     ?? GetString(parameters, "search");

        var source = context.Engine.GetAvailableCapabilities()
            .Where(c => context.Engine.CanExecute(c.Name));

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.ToLowerInvariant();
            source = source.Where(c =>
            {
                var name = c.Name?.ToLowerInvariant() ?? "";
                var cmd = c.Command?.ToLowerInvariant() ?? "";
                var desc = c.Description?.ToLowerInvariant() ?? "";
                var module = name.Contains('.') ? name.Split('.')[0] : name;
                var hints = (c.IntentHints != null) ? string.Join(" ", c.IntentHints).ToLowerInvariant() : "";
                return name.Contains(f) || cmd.Contains(f) || desc.Contains(f) || module.Contains(f) || hints.Contains(f);
            });
        }

        var capabilities = source
            .Select(c => new Dictionary<string, object?>
            {
                ["name"] = c.Name,
                ["command"] = c.Command,
                ["description"] = c.Description
            })
            .ToList();

        var msgFilter = string.IsNullOrWhiteSpace(filter) ? "" : $" (filtered by '{filter}')";
        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?> { ["capabilities"] = capabilities, ["count"] = capabilities.Count, ["filter"] = filter },
            Message: $"Listed {capabilities.Count} available command(s){msgFilter}.",
            IsDryRun: context.IsDryRun));
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}

public sealed class SystemLlmStatusHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var ollamaUrl = Environment.GetEnvironmentVariable("TORRENTBOT_OLLAMA_URL")
            ?? Environment.GetEnvironmentVariable("OLLAMA_HOST");
        var mode = string.IsNullOrWhiteSpace(ollamaUrl) ? "stub" : "ollama";
        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["mode"] = mode,
                ["planner"] = Environment.GetEnvironmentVariable("TORRENTBOT_OLLAMA_PLANNER_MODEL") ?? "stub",
                ["executor"] = Environment.GetEnvironmentVariable("TORRENTBOT_OLLAMA_EXECUTOR_MODEL") ?? "stub",
                ["responder"] = Environment.GetEnvironmentVariable("TORRENTBOT_OLLAMA_RESPONDER_MODEL") ?? "stub"
            },
            Message: $"LLM pipeline mode: {mode}",
            IsDryRun: context.IsDryRun));
    }
}

public sealed class SystemDiskUsageHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var root = Environment.GetEnvironmentVariable("TORRENTBOT_MEDIA_ROOT") ?? "/";
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)) ?? "/");
        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["path"] = drive.Name,
                ["total_gb"] = drive.TotalSize / 1_073_741_824.0,
                ["free_gb"] = drive.AvailableFreeSpace / 1_073_741_824.0,
                ["used_gb"] = (drive.TotalSize - drive.AvailableFreeSpace) / 1_073_741_824.0
            },
            Message: $"Disk usage for {drive.Name}",
            IsDryRun: context.IsDryRun));
    }
}

public sealed class SystemFindLargeFilesHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var root = Environment.GetEnvironmentVariable("TORRENTBOT_MEDIA_ROOT");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return Task.FromResult(new CapabilityResult(
                Success: true,
                Data: new Dictionary<string, object?> { ["files"] = Array.Empty<object>(), ["count"] = 0 },
                Message: "No media root configured; returning empty set.",
                IsDryRun: context.IsDryRun));
        }

        var minMb = int.TryParse(GetString(parameters, "min_mb"), out var parsed) ? parsed : 1024;
        var minBytes = minMb * 1024L * 1024L;
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(info => info.Length >= minBytes)
            .OrderByDescending(info => info.Length)
            .Take(20)
            .Select(info => new Dictionary<string, object?>
            {
                ["path"] = info.FullName,
                ["size"] = info.Length,
                ["size_mb"] = info.Length / 1_048_576.0
            })
            .ToList();

        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?> { ["files"] = files, ["count"] = files.Count, ["min_mb"] = minMb },
            Message: $"Found {files.Count} large file(s).",
            IsDryRun: context.IsDryRun));
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}

/// <summary>
/// Debug capability: returns the exact full planner prompt (system prompt + manifest + rules + user text)
/// that would be sent to the LLM. This enables manual prompt testing and inspection.
/// </summary>
public sealed class SystemLlmPromptDumpHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var type = (GetString(parameters, "type") ?? GetString(parameters, "prompt_type") ?? "planner").ToLowerInvariant();
        var text = GetString(parameters, "text")
                   ?? GetString(parameters, "query")
                   ?? GetString(parameters, "prompt")
                   ?? "";

        if (string.IsNullOrWhiteSpace(text) && type == "planner")
        {
            // Support bare /llm_prompt or /llm_prompt without text by using a sensible default for demo
            text = "jakie sa komendy do pobierania ?";
        }

        var scope = GetString(parameters, "scope") ?? "media";
        // include_context is advanced; for v1 we support it but default to false (snapshots add live state)
        var includeContext = bool.TryParse(GetString(parameters, "include_context"), out var ic) && ic;

        var engine = context.Engine;

        // Only capabilities the current user is allowed to use (matches real planner behavior)
        var visibleCapabilities = engine.GetAvailableCapabilities()
            .Where(c => engine.CanExecute(c.Name))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        var querySources = engine.GetQuerySourceManifests();

        // For maximum fidelity one would also populate ConversationContext with snapshots + history.
        // For now we keep it simple unless explicitly requested. Live snapshots can make the prompt
        // very large and session-specific.
        ConversationContext? conversation = null;
        if (includeContext)
        {
            // Minimal context with current user; snapshots will be empty unless we manually refresh.
            // This still gives the structural prompt + capability list the LLM sees.
            var sessionId = context.Request.TraceId ?? "debug";
            conversation = new ConversationContext(sessionId, context.User.UserId);
        }

        string fullPrompt;
        string promptKind = type;

        try
        {
            if (type == "planner" || type == "plan")
            {
                var planningRequest = new LlmPlanningRequest(
                    text,
                    visibleCapabilities,
                    querySources,
                    scope,
                    conversation,
                    RequestNumber: 1);
                fullPrompt = LlmSystemPromptBuilder.BuildPlannerPrompt(planningRequest);
                promptKind = "planner";
            }
            else if (type == "responder" || type == "response")
            {
                fullPrompt =
                    "You are TorrentBot, a helpful home media and automation assistant.\n" +
                    "User originally asked: <userText>\n" +
                    "The plan intent was: <plan.Intent>\n" +
                    "Execution produced this data: <resultSummary>\n\n" +
                    "Write a clear, friendly, concise reply in the same language as the user's request if possible. " +
                    "Include the important data (counts, names, statuses). Use bullet points or numbered lists when there are multiple items. " +
                    "Do not mention internal capabilities, plans, or JSON.";
                promptKind = "responder (template; filled at runtime with actual execution result)";
            }
            else if (type == "executor" || type == "validate")
            {
                fullPrompt =
                    "You are a strict plan validator for a home automation bot.\n" +
                    "The plan below was produced by an LLM planner using the known capability manifest.\n" +
                    "Check if every step.capability is a real registered capability and parameters look plausible.\n" +
                    "Respond ONLY with JSON: {\"approved\": true} or {\"approved\": false, \"error\": \"short reason\"}.\n\n" +
                    "<serialized PlanEnvelope here>";
                promptKind = "executor-validator (template)";
            }
            else
            {
                // default to planner
                var planningRequest = new LlmPlanningRequest(text, visibleCapabilities, querySources, scope, conversation, RequestNumber: 1);
                fullPrompt = LlmSystemPromptBuilder.BuildPlannerPrompt(planningRequest);
                promptKind = "planner";
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CapabilityResult(
                Success: false,
                Message: $"Failed to build prompt: {ex.Message}",
                IsDryRun: context.IsDryRun));
        }

        // Persist to a file so user can easily cat / copy / feed manually to ollama
        var debugDir = Path.Combine(Path.GetTempPath(), "homelynx-debug-prompts");
        Directory.CreateDirectory(debugDir);
        var fileName = $"planner-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.txt";
        var fullPath = Path.Combine(debugDir, fileName);
        File.WriteAllText(fullPath, fullPrompt);

        var preview = fullPrompt.Length > 1800 ? fullPrompt[..1800] + "\n... [truncated, see full file]" : fullPrompt;

        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["type"] = promptKind,
                ["text"] = text,
                ["scope"] = scope,
                ["capabilities_visible"] = visibleCapabilities.Count,
                ["query_sources"] = querySources.Count,
                ["prompt_length"] = fullPrompt.Length,
                ["full_prompt_path"] = fullPath,
                ["full_prompt"] = fullPrompt,   // full raw for --json / CLI consumers
                ["preview"] = preview
            },
            Message: $"Full {promptKind} prompt written to {fullPath} ({fullPrompt.Length} chars).",
            IsDryRun: context.IsDryRun));
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}
