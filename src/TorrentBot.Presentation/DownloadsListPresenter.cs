using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Presentation;

public sealed class DownloadsListPresenter : IArtifactPresenter
{
    public bool CanPresent(IExecutionArtifact artifact, RenderContext context) =>
        artifact is TextArtifact text
        && text.Data is Dictionary<string, object?> data
        && (data.ContainsKey("downloads") || data.ContainsKey("torrents") || data.ContainsKey("items"));

    public RenderedOutput Present(IExecutionArtifact artifact, RenderContext context)
    {
        var text = (TextArtifact)artifact;
        var data = (Dictionary<string, object?>)text.Data!;
        var raw = data.TryGetValue("downloads", out var downloads) ? downloads
            : data.TryGetValue("items", out var items) ? items
            : null;

        var formatHint = data.TryGetValue("formatHint", out var hint) ? hint?.ToString() : "downloads";
        var message = !string.IsNullOrWhiteSpace(text.Message)
            ? text.Message
            : ResponseFormatting.FormatListMessage(
                formatHint,
                raw is System.Collections.IEnumerable enumerable ? enumerable.Cast<object?>() : [],
                "Downloads");

        return new RenderedOutput(message);
    }
}