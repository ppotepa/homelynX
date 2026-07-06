using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TorrentBot.Adapters.Telegram.Sdk;

public static class TelegramSdkUpdateMapper
{
    public static ITelegramUpdate? Map(Update update)
    {
        if (update.Message is { Text: not null } message && message.From is not null)
        {
            return new TelegramUpdate(
                message.Chat.Id,
                message.From.Id.ToString(),
                message.Text,
                message.MessageId);
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