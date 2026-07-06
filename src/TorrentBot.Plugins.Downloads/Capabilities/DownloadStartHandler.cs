using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Jobs;
using TorrentBot.Contracts.ProcessManagers;

namespace TorrentBot.Plugins.Downloads.Capabilities;

public sealed class DownloadStartHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var provider = GetString(parameters, "provider") ?? (GetString(parameters, "url") is not null ? "url" : "torrent");
        var startRequest = new DownloadStartRequest(
            Provider: provider,
            Url: GetString(parameters, "url"),
            Magnet: GetString(parameters, "magnet"),
            Query: GetString(parameters, "query"),
            SearchIndex: GetInt(parameters, "index") ?? GetInt(parameters, "searchIndex"),
            Category: GetString(parameters, "category"),
            SavePath: GetString(parameters, "savePath"));

        if (context.IsDryRun)
        {
            var dryRunJobId = context.Engine.CreateJob(
                $"download.{provider}",
                startRequest,
                new JobOptions(SupportsPause: true, SupportsCancellation: true, Kind: JobKind.LongLived));

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
            new JobOptions(SupportsPause: true, SupportsCancellation: true, Kind: JobKind.LongLived));

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
}