namespace TorrentBot.Adapters.Telegram.Sdk;

public sealed record TelegramOutboundMessage(long ChatId, string Text, long? MessageId = null, IReadOnlyList<TelegramInlineButton>? Buttons = null);

public sealed record TelegramInlineButton(string Text, string CallbackData);

public interface ITelegramMessenger
{
    Task<long> SendTextAsync(long chatId, string text, IReadOnlyList<TelegramInlineButton>? buttons = null, CancellationToken ct = default);
    Task EditTextAsync(long chatId, long messageId, string text, CancellationToken ct = default);
    Task AnswerCallbackAsync(string callbackQueryId, string? text = null, CancellationToken ct = default);
    Task SendPhotoAsync(long chatId, byte[] content, string fileName, CancellationToken ct = default);
    Task SendDocumentAsync(long chatId, byte[] content, string fileName, CancellationToken ct = default);
    Task SendAudioAsync(long chatId, Stream content, string fileName, CancellationToken ct = default);
    Task SendVideoAsync(long chatId, Stream content, string fileName, CancellationToken ct = default);
    Task SendDocumentAsync(long chatId, Stream content, string fileName, CancellationToken ct = default);
    Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default);
}
