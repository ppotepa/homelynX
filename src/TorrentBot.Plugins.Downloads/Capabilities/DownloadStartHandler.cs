using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Jobs;
using TorrentBot.Contracts.ProcessManagers;
using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Plugins.Downloads.Capabilities;

public sealed class DownloadStartHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var url = GetString(parameters, "url");
        var magnet = GetString(parameters, "magnet");
        var requestedProvider = GetString(parameters, "provider");
        var provider = requestedProvider is null
            ? DetectProvider(url, magnet)
            : DownloaderProviderNormalizer.Normalize(requestedProvider);
        var startRequest = new DownloadStartRequest(
            Provider: provider,
            Url: url,
            Magnet: magnet,
            Query: GetString(parameters, "query"),
            SearchIndex: GetInt(parameters, "index") ?? GetInt(parameters, "searchIndex"),
            Category: GetString(parameters, "category"),
            SavePath: GetString(parameters, "savePath"),
            MediaFormat: GetString(parameters, "format") ?? GetString(parameters, "mediaFormat"),
            MediaQuality: GetString(parameters, "quality") ?? GetString(parameters, "mediaQuality"),
            MediaClipStart: GetString(parameters, "clipStart") ?? GetString(parameters, "mediaClipStart"),
            MediaClipEnd: GetString(parameters, "clipEnd") ?? GetString(parameters, "mediaClipEnd"),
            MediaSubtitles: GetString(parameters, "subtitles") ?? GetString(parameters, "mediaSubtitles"),
            OwnerUserId: context.Request.UserId);

        if (context.IsDryRun)
        {
            var dryRunJobId = context.Engine.CreateJob(
                $"download.{provider}",
                startRequest,
                new JobOptions(SupportsPause: provider.Equals("torrent", StringComparison.OrdinalIgnoreCase), SupportsCancellation: true, Kind: JobKind.LongLived));

            return new CapabilityResult(
                Success: true,
                Data: new Dictionary<string, object?>
                {
                    ["provider"] = provider,
                    ["jobId"] = dryRunJobId,
                    ["dryRun"] = true
                },
                Message: $"Dry-run: would start {provider} download",
                JobId: dryRunJobId,
                IsDryRun: true);
        }

        var processManager = context.Engine.GetService<IDownloadProcessManager>();
        if (processManager is null)
        {
            return new CapabilityResult(Success: false, Message: "Download process manager is not available.", IsDryRun: false);
        }

        var processJobId = await processManager.StartAsync(startRequest, context.Request, cancellationToken)
            .ConfigureAwait(false);
        var engineJobId = context.Engine.CreateJob(
            $"download.{provider}",
            startRequest,
            new JobOptions(SupportsPause: provider.Equals("torrent", StringComparison.OrdinalIgnoreCase), SupportsCancellation: true, Kind: JobKind.LongLived));

        context.Engine.UpdateJob(engineJobId, job => job with
        {
            Status = JobStatus.Running,
            ExternalId = processJobId,
            ExternalSystem = "download-process-manager"
        });

        return new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["provider"] = provider,
                ["jobId"] = engineJobId,
                ["processJobId"] = processJobId
            },
            Message: $"Started {provider} download",
            JobId: engineJobId,
            IsDryRun: false);
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static int? GetInt(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), out var number)
            ? number
            : null;

    private static string DetectProvider(string? url, string? magnet)
    {
        // Magnet URI zawsze oznacza torrent
        if (!string.IsNullOrWhiteSpace(magnet) && magnet.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            return "torrent";
        }

        // URL z Jackett oznacza torrent
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (url.Contains("/dl/", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("jackett", StringComparison.OrdinalIgnoreCase) ||
                url.Contains(".torrent", StringComparison.OrdinalIgnoreCase))
            {
                return "torrent";
            }

            if (IsSupportedMediaUrl(url))
            {
                return "media";
            }
        }

        // Domyślnie URL
        return "torrent";
    }

    private static bool IsSupportedMediaUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var hosts = new[]
        {
            "youtube.com", "youtu.be", "facebook.com", "fb.watch", "dailymotion.com", "dai.ly",
            "vimeo.com", "instagram.com", "tiktok.com"
        };
        return hosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }
}
