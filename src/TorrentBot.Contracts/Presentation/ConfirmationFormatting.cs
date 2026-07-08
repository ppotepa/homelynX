namespace TorrentBot.Contracts.Presentation;

public static class ConfirmationFormatting
{
    public static string FormatMessage(string message, string token, RenderChannel channel) =>
        channel == RenderChannel.Cli
            ? $"{message}\nConfirm with: --confirm {token}"
            : message;

    public static IReadOnlyList<RenderedButton>? FormatButtons(string token, RenderChannel channel) =>
        channel == RenderChannel.Telegram
            ?
            [
                new RenderedButton("Confirm", $"pending:yes:{token}"),
                new RenderedButton("Cancel", $"pending:no:{token}")
            ]
            : null;
}