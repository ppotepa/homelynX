namespace TorrentBot.Adapters.Telegram;

public sealed record TelegramUpdate(
    long ChatId,
    string UserId,
    string? Text = null,
    long? MessageId = null,
    string? CallbackData = null,
    TelegramAttachment? Attachment = null) : ITelegramUpdate
{
    public bool IsCallback => !string.IsNullOrWhiteSpace(CallbackData);
}

public sealed record TelegramAttachment(string FileId, string FileName, string? ContentType = null);
