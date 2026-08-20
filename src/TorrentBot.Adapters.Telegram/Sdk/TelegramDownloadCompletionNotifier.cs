using TorrentBot.Engine.Jobs;
using TorrentBot.Engine.Notifications;

namespace TorrentBot.Adapters.Telegram.Sdk;

public sealed class TelegramDownloadCompletionNotifier : IDownloadCompletionNotifier
{
    private readonly ITelegramMessenger _messenger;
    private readonly long _maxBytes;

    public TelegramDownloadCompletionNotifier(ITelegramMessenger messenger, long? maxBytes = null)
    {
        _messenger = messenger;
        _maxBytes = maxBytes
            ?? (long.TryParse(Environment.GetEnvironmentVariable("MEDIA_TELEGRAM_MAX_BYTES"), out var configured)
                ? configured
                : 47_185_920);
    }

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
            var size = new FileInfo(path).Length;
            if (_maxBytes > 0 && size > _maxBytes)
            {
                await _messenger.SendTextAsync(
                    chatId,
                    $"Pobieranie zakończone, ale plik ma {FormatBytes(size)} i przekracza limit Telegrama {FormatBytes(_maxBytes)}. Plik pozostaje zapisany w bibliotece mediów: {path}",
                    ct: ct).ConfigureAwait(false);
                return;
            }

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
        catch (Exception exception)
        {
            Console.Error.WriteLine($"telegram-download-notification failed path={completedEvent.ArtifactPath}: {exception.Message}");
            try
            {
                await _messenger.SendTextAsync(
                    chatId,
                    $"Pobieranie zakończone, ale nie udało się wysłać pliku do Telegrama. Plik pozostaje zapisany w bibliotece mediów: {completedEvent.ArtifactPath}",
                    ct: CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // There is no further notification channel available.
            }
        }
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.0} MB"
            : $"{bytes / 1024d:0.0} KB";
}
