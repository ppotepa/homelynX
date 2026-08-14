using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Plugins.System.Capabilities;

public sealed class SystemHelpHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(CapabilityContext context, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var filter = GetString(parameters, "filter") ?? GetString(parameters, "category") ?? GetString(parameters, "module") ?? GetString(parameters, "search");
        var source = context.Engine.GetAvailableCapabilities().Where(c => context.Engine.CanExecute(c.Name));
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.ToLowerInvariant();
            source = source.Where(c => c.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (c.Command?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false)
                || c.Description.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        var capabilities = source.Select(c => new Dictionary<string, object?>
        {
            ["name"] = c.Name, ["command"] = c.Command, ["description"] = c.Description
        }).ToList();
        return Task.FromResult(new CapabilityResult(true,
            new Dictionary<string, object?> { ["capabilities"] = capabilities, ["count"] = capabilities.Count, ["filter"] = filter },
            $"Listed {capabilities.Count} available command(s).", IsDryRun: context.IsDryRun));
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}

public sealed class SystemDiskUsageHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(CapabilityContext context, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var root = Environment.GetEnvironmentVariable("TORRENTBOT_MEDIA_ROOT") ?? "/";
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)) ?? "/");
        return Task.FromResult(new CapabilityResult(true, new Dictionary<string, object?>
        {
            ["path"] = drive.Name, ["total_gb"] = drive.TotalSize / 1_073_741_824.0,
            ["free_gb"] = drive.AvailableFreeSpace / 1_073_741_824.0,
            ["used_gb"] = (drive.TotalSize - drive.AvailableFreeSpace) / 1_073_741_824.0
        }, $"Disk usage for {drive.Name}", IsDryRun: context.IsDryRun));
    }
}

public sealed class SystemFindLargeFilesHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(CapabilityContext context, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        var root = Environment.GetEnvironmentVariable("TORRENTBOT_MEDIA_ROOT");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Task.FromResult(new CapabilityResult(true, new Dictionary<string, object?> { ["files"] = Array.Empty<object>(), ["count"] = 0 }, "No media root configured; returning empty set.", IsDryRun: context.IsDryRun));
        var minMb = int.TryParse(parameters.GetValueOrDefault("min_mb")?.ToString(), out var parsed) ? parsed : 1024;
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(path => new FileInfo(path))
            .Where(info => info.Length >= minMb * 1024L * 1024L).OrderByDescending(info => info.Length).Take(20)
            .Select(info => new Dictionary<string, object?> { ["path"] = info.FullName, ["size"] = info.Length, ["size_mb"] = info.Length / 1_048_576.0 }).ToList();
        return Task.FromResult(new CapabilityResult(true, new Dictionary<string, object?> { ["files"] = files, ["count"] = files.Count, ["min_mb"] = minMb }, $"Found {files.Count} large file(s).", IsDryRun: context.IsDryRun));
    }
}
