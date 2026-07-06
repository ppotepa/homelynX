using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Query;

namespace TorrentBot.Plugins.Media.Capabilities;

public sealed class MediaListHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var result = await context.Engine.QueryAsync(
            "media_files",
            new QuerySpec(Source: "media_files", Limit: 50),
            cancellationToken).ConfigureAwait(false);

        return new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["media"] = result.Items,
                ["count"] = result.Count
            },
            Message: $"{result.Count} media file(s) found",
            IsDryRun: context.IsDryRun);
    }
}