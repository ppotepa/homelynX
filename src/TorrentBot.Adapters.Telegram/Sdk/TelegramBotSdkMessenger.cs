using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TorrentBot.Adapters.Telegram.Sdk;

public sealed class TelegramBotSdkMessenger : ITelegramMessenger
{
    private readonly ITelegramBotClient _client;

    public TelegramBotSdkMessenger(ITelegramBotClient client) => _client = client;

    public async Task<long> SendTextAsync(long chatId, string text, IReadOnlyList<TelegramInlineButton>? buttons = null, CancellationToken ct = default)
    {
        InlineKeyboardMarkup? markup = null;
        if (buttons is { Count: > 0 })
        {
            markup = new InlineKeyboardMarkup(buttons.Select(b => new[] { InlineKeyboardButton.WithCallbackData(b.Text, b.CallbackData) }));
        }

        var message = await _client.SendMessage(chatId, text, replyMarkup: markup, cancellationToken: ct).ConfigureAwait(false);
        return message.MessageId;
    }

    public Task EditTextAsync(long chatId, long messageId, string text, CancellationToken ct = default) =>
        _client.EditMessageText(chatId, (int)messageId, text, cancellationToken: ct);

    public Task AnswerCallbackAsync(string callbackQueryId, string? text = null, CancellationToken ct = default) =>
        _client.AnswerCallbackQuery(callbackQueryId, text, cancellationToken: ct);

    public Task SendPhotoAsync(long chatId, byte[] content, string fileName, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(content);
        return _client.SendPhoto(chatId, InputFile.FromStream(stream, fileName), cancellationToken: ct);
    }

    public Task SendDocumentAsync(long chatId, byte[] content, string fileName, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(content);
        return _client.SendDocument(chatId, InputFile.FromStream(stream, fileName), cancellationToken: ct);
    }

    public async Task SendAudioAsync(long chatId, Stream content, string fileName, CancellationToken ct = default) =>
        await _client.SendAudio(chatId, InputFile.FromStream(content, fileName), cancellationToken: ct).ConfigureAwait(false);

    public async Task SendVideoAsync(long chatId, Stream content, string fileName, CancellationToken ct = default) =>
        await _client.SendVideo(chatId, InputFile.FromStream(content, fileName), cancellationToken: ct).ConfigureAwait(false);

    public async Task SendDocumentAsync(long chatId, Stream content, string fileName, CancellationToken ct = default) =>
        await _client.SendDocument(chatId, InputFile.FromStream(content, fileName), cancellationToken: ct).ConfigureAwait(false);

    public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken ct = default)
    {
        var file = await _client.GetFile(fileId, cancellationToken: ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(file.FilePath)) throw new InvalidOperationException("Telegram file path is unavailable.");
        await using var output = new MemoryStream();
        await _client.DownloadFile(file.FilePath, output, cancellationToken: ct).ConfigureAwait(false);
        return output.ToArray();
    }
}
