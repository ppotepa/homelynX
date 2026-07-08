using TorrentBot.Contracts.Context;
using TorrentBot.Plugins.Downloads;
using TorrentBot.Plugins.Torrent;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class TorrentSearchConversationStateTests
{
    [Fact]
    public void Save_getPage_setPage_and_clear_round_trip_through_conversation_snapshot()
    {
        var context = new ConversationContext("chat-1", "user-1");
        var results = new List<DownloadSearchResult>
        {
            new("r0", "Alpha", "torrent", 100, 10, "magnet:a", null),
            new("r1", "Beta", "torrent", 200, 5, "magnet:b", null),
            new("r2", "Gamma", "torrent", 300, 2, "magnet:c", null),
            new("r3", "Delta", "torrent", 400, 1, "magnet:d", null),
            new("r4", "Epsilon", "torrent", 500, 0, "magnet:e", null),
            new("r5", "Zeta", "torrent", 600, 0, "magnet:f", null)
        };

        TorrentSearchConversationState.Save(context, "ubuntu", results, page: 0, pageSize: 2);
        Assert.True(TorrentSearchConversationState.TryGet(context, out var session));
        Assert.Equal("ubuntu", session.Query);
        Assert.Equal(6, session.Results.Count);
        Assert.Equal(0, session.Page);

        var page0 = TorrentSearchConversationState.GetPage(context);
        Assert.Equal(2, page0.Count);
        Assert.Equal("Alpha", page0[0].Name);
        Assert.Equal("Beta", page0[1].Name);

        TorrentSearchConversationState.SetPage(context, 1);
        Assert.True(TorrentSearchConversationState.TryGet(context, out session));
        Assert.Equal(1, session.Page);

        var page1 = TorrentSearchConversationState.GetPage(context);
        Assert.Equal(2, page1.Count);
        Assert.Equal("Gamma", page1[0].Name);
        Assert.Equal("Delta", page1[1].Name);

        var snapshot = context.GetSnapshot(TorrentSearchConversationState.SnapshotSource);
        Assert.NotNull(snapshot);
        Assert.Equal("ubuntu", snapshot!.State["query"]);
        Assert.Equal(6, snapshot.State["count"]);

        TorrentSearchConversationState.Clear(context);
        Assert.False(TorrentSearchConversationState.TryGet(context, out _));
        Assert.Null(context.GetSnapshot(TorrentSearchConversationState.SnapshotSource));
    }
}