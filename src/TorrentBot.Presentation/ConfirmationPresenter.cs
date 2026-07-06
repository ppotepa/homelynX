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
        IReadOnlyList<RenderedButton>? buttons = context.Channel == RenderChannel.Telegram
            ? new[]
            {
                new RenderedButton("Confirm", $"confirm:{confirm.Token}"),
                new RenderedButton("Cancel", $"cancel:{confirm.Token}")
            }
            : null;

        var text = context.Channel == RenderChannel.Cli
            ? $"{confirm.Message}\nConfirm with: --confirm {confirm.Token}"
            : confirm.Message;

        return new RenderedOutput(text, buttons, ExitCode: 1);
    }
}