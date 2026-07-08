using TorrentBot.Plugins.Torrent;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class TorrentSearchIndexTests
{
    [Theory]
    [InlineData(1, 0, 5, 10, 0)]
    [InlineData(2, 0, 5, 10, 1)]
    [InlineData(1, 1, 5, 10, 5)]
    [InlineData(3, 1, 5, 10, 7)]
    public void TryToGlobalIndex_maps_displayed_1_based_index_to_storage_index(
        int displayed,
        int page,
        int pageSize,
        int total,
        int expectedGlobal)
    {
        Assert.True(TorrentSearchIndex.TryToGlobalIndex(displayed, page, pageSize, total, out var global));
        Assert.Equal(expectedGlobal, global);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    public void TryToGlobalIndex_rejects_invalid_displayed_index(int displayed)
    {
        Assert.False(TorrentSearchIndex.TryToGlobalIndex(displayed, 0, 5, 3, out _));
    }
}