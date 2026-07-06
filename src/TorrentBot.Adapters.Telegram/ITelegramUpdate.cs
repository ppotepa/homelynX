namespace TorrentBot.Adapters.Telegram;

public interface ITelegramUpdate
{
    long ChatId { get; }
    long? MessageId { get; }
    string? Text { get; }
    string? CallbackData { get; }
    string UserId { get; }
    bool IsCallback { get; }
}