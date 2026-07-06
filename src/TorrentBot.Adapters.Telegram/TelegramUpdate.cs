namespace TorrentBot.Adapters.Telegram;

public sealed record TelegramUpdate(
    long ChatId,
    string UserId,
    string? Text = null,
    long? MessageId = null,
    string? CallbackData = null) : ITelegramUpdate
{
    public bool IsCallback => !string.IsNullOrWhiteSpace(CallbackData);
}