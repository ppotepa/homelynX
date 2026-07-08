using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Presentation;

public sealed class ConfirmationPresenter : IArtifactPresenter
{
    public bool CanPresent(IExecutionArtifact artifact, RenderContext context) =>
        artifact is ConfirmationArtifact;

    public RenderedOutput Present(IExecutionArtifact artifact, RenderContext context)
    {
        var confirm = (ConfirmationArtifact)artifact;
        var text = ConfirmationFormatting.FormatMessage(confirm.Message, confirm.Token, context.Channel);
        var buttons = ConfirmationFormatting.FormatButtons(confirm.Token, context.Channel);
        return new RenderedOutput(text, buttons, ExitCode: 1);
    }
}