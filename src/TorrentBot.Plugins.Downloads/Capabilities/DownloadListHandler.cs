using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Query;

namespace TorrentBot.Plugins.Downloads.Capabilities;

public sealed class DownloadListHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var result = await context.Engine.QueryAsync(
            "downloads",
            new QuerySpec(Source: "downloads", Limit: 50),
            cancellationToken).ConfigureAwait(false);

        return new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["downloads"] = result.Items,
                ["count"] = result.Count
            },
            Message: $"{result.Count} download(s) found",
            IsDryRun: context.IsDryRun);
    }
}