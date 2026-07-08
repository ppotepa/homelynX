using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Presentation;

public sealed class SearchResultsPresenter : IArtifactPresenter
{
    public bool CanPresent(IExecutionArtifact artifact, RenderContext context) =>
        artifact is SearchResultsArtifact;

    public RenderedOutput Present(IExecutionArtifact artifact, RenderContext context) =>
        SearchResultsFormatting.Render((SearchResultsArtifact)artifact, context);
}