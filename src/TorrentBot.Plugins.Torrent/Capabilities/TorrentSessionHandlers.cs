using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Plugins.Torrent.Capabilities;

public sealed class TorrentMoreResultsHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var store = context.Engine.GetService<TorrentSearchSessionStore>();
        if (store is null || !store.TryGet(context.User.UserId, out var session))
        {
            return Task.FromResult(new CapabilityResult(Success: false, Message: "No active torrent search session."));
        }

        var nextPage = session.Page + 1;
        var page = store.GetPage(context.User.UserId, nextPage);
        if (page.Count == 0)
        {
            return Task.FromResult(new CapabilityResult(Success: false, Message: "No more search results."));
        }

        store.SetPage(context.User.UserId, nextPage);
        return Task.FromResult(BuildResults(session.Query, session.Results, nextPage, "More torrent results"));
    }

    internal static CapabilityResult BuildResults(
        string query,
        IReadOnlyList<DownloadSearchResult> allResults,
        int pageIndex,
        string message) =>
        new(
            Success: true,
            Data: SearchResultsBuilder.BuildPageData(query, allResults, pageIndex),
            Message: message);
}

public sealed class TorrentSelectResultHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var store = context.Engine.GetService<TorrentSearchSessionStore>();
        if (store is null || !store.TryGet(context.User.UserId, out var session))
        {
            return new CapabilityResult(Success: false, Message: "No active torrent search session.");
        }

        var index = GetInt(parameters, "index") ?? GetInt(parameters, "number");
        if (index is null)
        {
            return new CapabilityResult(Success: false, Message: "Parameter 'index' is required.");
        }

        var pageRelative = index.Value >= 1 ? index.Value - 1 : index.Value;
        var globalIndex = session.Page * session.PageSize + pageRelative;
        if (globalIndex < 0 || globalIndex >= session.Results.Count)
        {
            return new CapabilityResult(Success: false, Message: $"Index {index} is out of range.");
        }

        var selected = session.Results[globalIndex];
        var registry = context.Engine.GetService<DownloaderRegistry>();
        if (registry is null)
        {
            return new CapabilityResult(Success: false, Message: "Download registry is not available.");
        }

        if (context.IsDryRun)
        {
            return new CapabilityResult(
                Success: true,
                Data: new Dictionary<string, object?> { ["selected"] = selected, ["dryRun"] = true },
                Message: $"Dry-run: would start download for '{selected.Name}'",
                IsDryRun: true);
        }

        var downloader = registry.GetRequired("torrent");
        var ticket = await downloader.StartAsync(
            new DownloadStartRequest(Provider: "torrent", Magnet: selected.MagnetUri ?? selected.Url, Query: session.Query, SearchIndex: globalIndex),
            cancellationToken).ConfigureAwait(false);

        return new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["artifactKind"] = "download_started",
                ["selected"] = selected,
                ["ticket"] = ticket,
                ["provider"] = "torrent",
                ["jobId"] = ticket.DownloadId
            },
            Message: $"Started download for '{selected.Name}'");
    }

    private static int? GetInt(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), out var number)
            ? number
            : null;
}

public sealed class TorrentCancelSearchHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var store = context.Engine.GetService<TorrentSearchSessionStore>();
        store?.Clear(context.User.UserId);
        return Task.FromResult(new CapabilityResult(Success: true, Message: "Torrent search session cleared."));
    }
}

public sealed class TorrentDownloadCandidateHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var title = GetString(parameters, "title") ?? GetString(parameters, "query") ?? GetString(parameters, "name");
        if (string.IsNullOrWhiteSpace(title))
        {
            return new CapabilityResult(Success: false, Message: "Parameter 'title' is required.");
        }

        var jackett = context.Engine.GetService<IJackettClient>();
        if (jackett is null)
        {
            return new CapabilityResult(Success: false, Message: "Jackett client is not available.");
        }

        var results = await jackett.SearchAsync(title, cancellationToken).ConfigureAwait(false);
        if (results.Count == 0)
        {
            return new CapabilityResult(Success: false, Message: $"No torrent candidates found for '{title}'.");
        }

        var ordered = results
            .OrderByDescending(r => r.Seeders)
            .Select((result, index) => new DownloadSearchResult(
                $"candidate-{index}",
                result.Title,
                "torrent",
                result.SizeBytes,
                result.Seeders,
                result.MagnetUri,
                result.DownloadUrl))
            .ToList();
        context.Engine.GetService<TorrentSearchSessionStore>()?.Save(context.User.UserId, title, ordered);
        var select = new TorrentSelectResultHandler();

        return await select.ExecuteAsync(
            context,
            new Dictionary<string, object?> { ["index"] = 0 },
            cancellationToken).ConfigureAwait(false);
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}