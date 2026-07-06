using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Plugins.Torrent.Capabilities;

public sealed class TorrentSearchHandler : ICapabilityHandler
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

        var registry = context.Engine.GetService<DownloaderRegistry>();
        var downloader = registry?.Get("torrent");
        if (downloader is null)
        {
            var jackett = context.Engine.GetService<IJackettClient>();
            if (jackett is null)
            {
                return new CapabilityResult(Success: false, Message: "Torrent search is not available.");
            }

            var directResults = await jackett.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            var mapped = directResults.Select((result, index) => new DownloadSearchResult(
                $"direct-{index}",
                result.Title,
                "torrent",
                result.SizeBytes,
                result.Seeders,
                result.MagnetUri,
                result.DownloadUrl)).ToList();
            context.Engine.GetService<TorrentSearchSessionStore>()?.Save(context.User.UserId, query, mapped);
            return new CapabilityResult(
                Success: true,
                Data: SearchResultsBuilder.BuildPageData(query, mapped, page: 0),
                Message: $"Found {directResults.Count} torrent result(s)",
                IsDryRun: context.IsDryRun);
        }

        var results = await downloader.SearchAsync(new DownloadSearchRequest(query, "torrent"), cancellationToken)
            .ConfigureAwait(false);

        var store = context.Engine.GetService<TorrentSearchSessionStore>();
        store?.Save(context.User.UserId, query, results.Items);
        var pageData = SearchResultsBuilder.BuildPageData(query, results.Items, page: 0);

        return new CapabilityResult(
            Success: true,
            Data: pageData,
            Message: $"Found {results.Items.Count} torrent result(s)",
            IsDryRun: context.IsDryRun);
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}

public sealed class TorrentListHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var qbit = context.Engine.GetService<IQBittorrentClient>();
        if (qbit is null)
        {
            return new CapabilityResult(Success: false, Message: "qBittorrent client is not available.");
        }

        var torrents = await qbit.ListTorrentsAsync(cancellationToken).ConfigureAwait(false);
        return new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["torrents"] = torrents,
                ["count"] = torrents.Count
            },
            Message: $"{torrents.Count} torrent(s) in qBittorrent",
            IsDryRun: context.IsDryRun);
    }
}

public sealed class TorrentPauseHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        TorrentControl.ExecuteTorrentControlAsync(context, parameters, "pause", cancellationToken);
}

public sealed class TorrentResumeHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        TorrentControl.ExecuteTorrentControlAsync(context, parameters, "resume", cancellationToken);
}

public sealed class TorrentDeleteHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        TorrentControl.ExecuteTorrentControlAsync(context, parameters, "delete", cancellationToken);
}

internal static class TorrentControl
{
    public static async Task<CapabilityResult> ExecuteTorrentControlAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        string command,
        CancellationToken cancellationToken)
    {
        var hash = GetString(parameters, "hash") ?? GetString(parameters, "id");
        if (string.IsNullOrWhiteSpace(hash))
        {
            return new CapabilityResult(Success: false, Message: "Parameter 'hash' is required.");
        }

        if (context.IsDryRun)
        {
            return new CapabilityResult(
                Success: true,
                Message: $"Dry-run: would {command} torrent {hash}",
                Data: new Dictionary<string, object?> { ["hash"] = hash, ["command"] = command },
                IsDryRun: true);
        }

        var qbit = context.Engine.GetService<IQBittorrentClient>();
        if (qbit is null)
        {
            return new CapabilityResult(Success: false, Message: "qBittorrent client is not available.");
        }

        switch (command)
        {
            case "pause":
                await qbit.PauseAsync(hash, cancellationToken).ConfigureAwait(false);
                break;
            case "resume":
                await qbit.ResumeAsync(hash, cancellationToken).ConfigureAwait(false);
                break;
            case "delete":
                var deleteFiles = GetBool(parameters, "deleteFiles") ?? false;
                await qbit.DeleteAsync(hash, deleteFiles, cancellationToken).ConfigureAwait(false);
                break;
        }

        return new CapabilityResult(
            Success: true,
            Message: $"Torrent {command} applied",
            Data: new Dictionary<string, object?> { ["hash"] = hash, ["command"] = command });
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool? GetBool(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) && bool.TryParse(value?.ToString(), out var result)
            ? result
            : null;
}