using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Engine.Pipeline.ResponseArtifacts;

internal sealed class DownloadStartedArtifactBuilder : IResponseArtifactBuilder
{
    public string ArtifactKind => "download_started";

    public IReadOnlyList<IExecutionArtifact> Build(
        ResponseConstructionSpec? spec,
        ExecutionResult result,
        ConversationContext? conversation)
    {
        if (result.CapabilityResult?.Data is not Dictionary<string, object?> data)
        {
            return [new TextArtifact(result.CapabilityResult?.Message ?? "Download started.", result.CapabilityResult?.Data)];
        }

        if (string.IsNullOrWhiteSpace(spec?.SelectedKey)
            || !data.TryGetValue(spec.SelectedKey, out var selected))
        {
            return [new TextArtifact(result.CapabilityResult?.Message ?? "Download started.", data)];
        }

        var name = ResolveSelectedName(selected) ?? "download";

        var provider = data.TryGetValue("provider", out var pr) ? pr?.ToString() ?? "torrent" : "torrent";
        var jobId = result.CapabilityResult?.JobId
            ?? (data.TryGetValue("jobId", out var j) ? j?.ToString() : null);
        string? downloadId = null;
        if (data.TryGetValue("ticket", out var ticket) && ticket is Dictionary<string, object?> tk
            && tk.TryGetValue("downloadId", out var did))
        {
            downloadId = did?.ToString();
        }
        else if (data.TryGetValue("processJobId", out var pj))
        {
            downloadId = pj?.ToString();
        }

        return [new DownloadStartedArtifact(name, provider, jobId, downloadId)];
    }

    private static string? ResolveSelectedName(object? selected) =>
        selected switch
        {
            null => null,
            Dictionary<string, object?> dict when dict.TryGetValue("name", out var sn) => sn?.ToString(),
            _ when selected.GetType().GetProperty("Name")?.GetValue(selected) is { } name => name.ToString(),
            _ => null
        };
}