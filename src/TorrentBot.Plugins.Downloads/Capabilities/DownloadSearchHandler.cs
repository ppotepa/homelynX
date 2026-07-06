using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Plugins.Downloads.Capabilities;

public sealed class DownloadSearchHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var query = GetString(parameters, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return new CapabilityResult(Success: false, Message: "Parameter 'query' is required.", IsDryRun: context.IsDryRun);
        }

        var provider = GetString(parameters, "provider") ?? "torrent";
        var registry = context.Engine.GetService<DownloaderRegistry>();
        if (registry is null)
        {
            return new CapabilityResult(Success: false, Message: "Downloader registry is not available.", IsDryRun: context.IsDryRun);
        }

        var downloader = registry.GetRequired(provider);
        var results = await downloader.SearchAsync(new DownloadSearchRequest(query, provider), cancellationToken)
            .ConfigureAwait(false);

        return new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["provider"] = provider,
                ["query"] = query,
                ["results"] = results.Items.Select(r => new Dictionary<string, object?>
                {
                    ["id"] = r.Id,
                    ["name"] = r.Name,
                    ["provider"] = r.Provider,
                    ["size"] = r.SizeBytes,
                    ["seeders"] = r.Seeders,
                    ["magnet"] = r.MagnetUri,
                    ["url"] = r.Url
                }).ToList(),
                ["count"] = results.Items.Count
            },
            Message: $"Found {results.Items.Count} result(s) for '{query}'",
            IsDryRun: context.IsDryRun);
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}