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
}