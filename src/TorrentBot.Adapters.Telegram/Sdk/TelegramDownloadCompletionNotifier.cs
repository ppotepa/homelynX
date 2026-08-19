using TorrentBot.Engine.Jobs;
using TorrentBot.Engine.Notifications;

namespace TorrentBot.Adapters.Telegram.Sdk;

public sealed class TelegramDownloadCompletionNotifier : IDownloadCompletionNotifier
{
    private readonly ITelegramMessenger _messenger;

    public TelegramDownloadCompletionNotifier(ITelegramMessenger messenger) => _messenger = messenger;

    public void Notify(DownloadCompletedEvent completedEvent)
    {
        if (!long.TryParse(completedEvent.ChatId, out var chatId)
            || string.IsNullOrWhiteSpace(completedEvent.ArtifactPath)
            || !File.Exists(completedEvent.ArtifactPath))
        {
            return;
        }

        _ = SendAsync(chatId, completedEvent, CancellationToken.None);
    }

    private async Task SendAsync(long chatId, DownloadCompletedEvent completedEvent, CancellationToken ct)
    {
        try
        {
            var path = completedEvent.ArtifactPath!;
            await using var stream = File.OpenRead(path);
            var fileName = Path.GetFileName(path);
            if (string.Equals(completedEvent.MediaFormat, "mp3", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                await _messenger.SendAudioAsync(chatId, stream, fileName, ct).ConfigureAwait(false);
            }
            else if (string.Equals(completedEvent.MediaFormat, "subtitles", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
            {
                await _messenger.SendDocumentAsync(chatId, stream, fileName, ct).ConfigureAwait(false);
            }
            else
            {
                await _messenger.SendVideoAsync(chatId, stream, fileName, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            // The file remains in Jellyfin even if Telegram rejects the upload.
        }
    }
}
