using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Presentation;

public sealed class ErrorPresenter : IArtifactPresenter
{
    public bool CanPresent(IExecutionArtifact artifact, RenderContext context) =>
        artifact is ErrorArtifact;

    public RenderedOutput Present(IExecutionArtifact artifact, RenderContext context)
    {
        var error = (ErrorArtifact)artifact;
        if (error.Code == "not_found"
            && error.Message.Contains("not resolved", StringComparison.OrdinalIgnoreCase))
        {
            return new RenderedOutput(
                "Nieznana komenda. Uzyj /help lub /list aby zobaczyc dostepne komendy.",
                ExitCode: 1);
        }

        return new RenderedOutput(error.Message, ExitCode: 1);
    }
}