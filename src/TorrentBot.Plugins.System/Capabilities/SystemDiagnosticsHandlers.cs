using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Plugins.System.Capabilities;

public sealed class SystemHelpHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var capabilities = context.Engine.GetAvailableCapabilities()
            .Where(c => context.Engine.CanExecute(c.Name))
            .Select(c => new Dictionary<string, object?>
            {
                ["name"] = c.Name,
                ["command"] = c.Command,
                ["description"] = c.Description
            })
            .ToList();

        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?> { ["capabilities"] = capabilities, ["count"] = capabilities.Count },
            Message: $"Listed {capabilities.Count} available command(s).",
            IsDryRun: context.IsDryRun));
    }
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