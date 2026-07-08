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
        var text = DownloadStartedFormatting.FormatMessage(
            download.Name,
            download.Provider,
            download.JobId,
            download.DownloadId);
        return new RenderedOutput(text);
    }
}