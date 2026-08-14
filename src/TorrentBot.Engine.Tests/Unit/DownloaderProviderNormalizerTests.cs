using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class DownloaderProviderNormalizerTests
{
    [Theory]
    [InlineData(null, "torrent")]
    [InlineData("", "torrent")]
    [InlineData("   ", "torrent")]
    [InlineData("Jackett", "torrent")]
    [InlineData("all", "torrent")]
    [InlineData("torrent", "torrent")]
    [InlineData("url", "url")]
    public void Normalize_maps_to_registered_providers(string? input, string expected) =>
        Assert.Equal(expected, DownloaderProviderNormalizer.Normalize(input));
}