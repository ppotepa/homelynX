using TorrentBot.Contracts.Artifacts;

namespace TorrentBot.Contracts.Presentation;

public interface IArtifactPresenter
{
    bool CanPresent(IExecutionArtifact artifact, RenderContext context);
    RenderedOutput Present(IExecutionArtifact artifact, RenderContext context);
}