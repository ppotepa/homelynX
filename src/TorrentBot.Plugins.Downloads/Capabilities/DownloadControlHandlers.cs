using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.ProcessManagers;
using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Plugins.Downloads.Capabilities;

public sealed class DownloadPauseHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        return await ExecuteControlAsync(context, parameters, "pause", cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<CapabilityResult> ExecuteControlAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        string command,
        CancellationToken cancellationToken)
    {
        var jobId = GetString(parameters, "jobId");
        var downloadId = GetString(parameters, "id") ?? GetString(parameters, "hash");

        if (context.IsDryRun)
        {
            return new CapabilityResult(
                Success: true,
                Message: $"Dry-run: would {command} download",
                Data: new Dictionary<string, object?> { ["jobId"] = jobId, ["downloadId"] = downloadId, ["command"] = command },
                IsDryRun: true);
        }

        if (!string.IsNullOrWhiteSpace(jobId))
        {
            var processManager = context.Engine.GetService<IDownloadProcessManager>();
            if (processManager is null)
            {
                return new CapabilityResult(Success: false, Message: "Download process manager is not available.");
            }

            await processManager.HandleCommandAsync(jobId, command, parameters, context.Request, cancellationToken)
                .ConfigureAwait(false);

            return new CapabilityResult(
                Success: true,
                Message: $"Download process {command} applied",
                Data: new Dictionary<string, object?> { ["jobId"] = jobId, ["command"] = command });
        }

        if (!string.IsNullOrWhiteSpace(downloadId))
        {
            var qbit = context.Engine.GetService<IQBittorrentClient>();
            if (qbit is not null)
            {
                switch (command)
                {
                    case "pause":
                        await qbit.PauseAsync(downloadId, cancellationToken).ConfigureAwait(false);
                        break;
                    case "resume":
                        await qbit.ResumeAsync(downloadId, cancellationToken).ConfigureAwait(false);
                        break;
                    case "cancel":
                        await qbit.DeleteAsync(downloadId, deleteFiles: false, cancellationToken).ConfigureAwait(false);
                        break;
                }

                return new CapabilityResult(
                    Success: true,
                    Message: $"Torrent {command} applied",
                    Data: new Dictionary<string, object?> { ["downloadId"] = downloadId, ["command"] = command });
            }

            var registry = context.Engine.GetService<DownloaderRegistry>();
            var provider = GetString(parameters, "provider") ?? "url";
            if (registry?.Get(provider) is { } downloader)
            {
                switch (command)
                {
                    case "pause":
                        await downloader.PauseAsync(downloadId, cancellationToken).ConfigureAwait(false);
                        break;
                    case "resume":
                        await downloader.ResumeAsync(downloadId, cancellationToken).ConfigureAwait(false);
                        break;
                    case "cancel":
                        await downloader.CancelAsync(downloadId, cancellationToken).ConfigureAwait(false);
                        break;
                }

                return new CapabilityResult(
                    Success: true,
                    Message: $"{provider} download {command} applied",
                    Data: new Dictionary<string, object?> { ["downloadId"] = downloadId, ["provider"] = provider });
            }
        }

        return new CapabilityResult(Success: false, Message: "Parameter 'jobId' or 'id' is required.");
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> parameters, string key) =>
        parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
}

public sealed class DownloadResumeHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        DownloadPauseHandler.ExecuteControlAsync(context, parameters, "resume", cancellationToken);
}

public sealed class DownloadCancelHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        DownloadPauseHandler.ExecuteControlAsync(context, parameters, "cancel", cancellationToken);
}