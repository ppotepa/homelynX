using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Plugins.Downloads.Capabilities;

public sealed class DownloadStartUrlHandler : ICapabilityHandler
{
    private readonly DownloadStartHandler _inner = new();

    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var url = GetString(parameters, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult(new CapabilityResult(Success: false, Message: "Parameter 'url' is required."));
        }

        var merged = new Dictionary<string, object?>(parameters, StringComparer.Ordinal)
        {
            ["provider"] = "url",
            ["url"] = url
        };

        return _inner.ExecuteAsync(context, merged, cancellationToken);
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}