using TorrentBot.Contracts.Context;
using TorrentBot.Engine.Context;

namespace TorrentBot.Plugins.Torrent;

internal static class TorrentSearchContext
{
    public static ConversationContext Resolve(CapabilityContext context)
    {
        var store = context.Engine.GetService<ConversationContextStore>()
            ?? throw new InvalidOperationException("ConversationContextStore is not available.");
        var sessionId = context.Request.ChatId ?? context.User.UserId;
        return store.GetOrCreate(sessionId, context.User.UserId);
    }
}