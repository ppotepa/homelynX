using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Presentation;

public sealed class TextPresenter : IArtifactPresenter
{
    public bool CanPresent(IExecutionArtifact artifact, RenderContext context) =>
        artifact is TextArtifact;

    public RenderedOutput Present(IExecutionArtifact artifact, RenderContext context)
    {
        var text = (TextArtifact)artifact;
        return new RenderedOutput(text.Message);
    }
}