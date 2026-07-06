using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Presentation;

public sealed class DownloadStartedPresenter : IArtifactPresenter
{
    public bool CanPresent(IExecutionArtifact artifact, RenderContext context) =>
        artifact is DownloadStartedArtifact;

    public RenderedOutput Present(IExecutionArtifact artifact, RenderContext context)
    {
        var download = (DownloadStartedArtifact)artifact;
        var text = $"Pobieranie rozpoczete: {download.Name} ({download.Provider})";
        if (!string.IsNullOrWhiteSpace(download.JobId))
        {
            text += $"\nJob: {download.JobId}";
        }

        return new RenderedOutput(text);
    }
}