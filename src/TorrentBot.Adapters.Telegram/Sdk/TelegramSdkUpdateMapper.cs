using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TorrentBot.Adapters.Telegram.Sdk;

public static class TelegramSdkUpdateMapper
{
    public static ITelegramUpdate? Map(Update update)
    {
        if (update.Message is { From: not null } message)
        {
            var attachment = message.Document is { } document
                ? new TelegramAttachment(document.FileId, document.FileName ?? "attachment.bin", document.MimeType)
                : message.Audio is { } audio
                    ? new TelegramAttachment(audio.FileId, audio.FileName ?? "audio.bin", audio.MimeType)
                    : null;
            var text = message.Text ?? message.Caption;
            if (text is null && attachment is not null && (attachment.FileName.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || attachment.FileName.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))) text = "/chiptune";
            if (text is null && attachment is null) return null;
            return new TelegramUpdate(
                message.Chat.Id,
                message.From.Id.ToString(),
                text,
                message.MessageId,
                Attachment: attachment);
        }

        if (update.CallbackQuery is { Data: not null, From: not null } callback && callback.Message is not null)
        {
            return new TelegramUpdate(
                callback.Message.Chat.Id,
                callback.From.Id.ToString(),
                CallbackData: callback.Data,
                MessageId: callback.Message.MessageId);
        }

        return null;
    }

    public static bool IsCommand(Update update, out string command)
    {
        command = string.Empty;
        if (update.Type != UpdateType.Message || update.Message?.Entities is null || update.Message.Text is null)
        {
            return false;
        }

        var entity = update.Message.Entities.FirstOrDefault(e => e.Type == MessageEntityType.BotCommand);
        if (entity is null)
        {
            return false;
        }

        command = update.Message.Text[entity.Offset..(entity.Offset + entity.Length)].ToLowerInvariant();
        return true;
    }
}
