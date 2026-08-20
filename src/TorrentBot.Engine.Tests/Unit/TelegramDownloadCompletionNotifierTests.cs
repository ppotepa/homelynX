using TorrentBot.Adapters.Telegram.Sdk;
using TorrentBot.Engine.Jobs;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class TelegramDownloadCompletionNotifierTests
{
    [Fact]
    public async Task Oversized_media_sends_explanation_instead_of_silent_failure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"homelynx-notifier-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, new byte[1024]);
        try
        {
            var messenger = new RecordingTelegramMessenger();
            new TelegramDownloadCompletionNotifier(messenger, maxBytes: 100).Notify(
                new DownloadCompletedEvent("job", "clip", "media", "user", "42", path, "mp4"));

            for (var attempt = 0; attempt < 20 && messenger.Sent.Count == 0; attempt++)
            {
                await Task.Delay(10);
            }

            var message = Assert.Single(messenger.Sent);
            Assert.Contains("przekracza limit Telegrama", message.Text, StringComparison.Ordinal);
            Assert.Contains(path, message.Text, StringComparison.Ordinal);
            Assert.Empty(messenger.Videos);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
