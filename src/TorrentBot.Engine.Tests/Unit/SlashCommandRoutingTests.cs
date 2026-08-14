using TorrentBot.Engine;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class SlashCommandRoutingTests
{
    [Theory]
    [InlineData("/download_search", "torrent.search")]
    [InlineData("/list", "system.help")]
    [InlineData("/commands", "system.help")]
    public void ResolveCapabilityOverride_maps_shared_commands(string command, string capability) =>
        Assert.Equal(capability, SlashCommandRouting.ResolveCapabilityOverride(command));

    [Fact]
    public void ParseParameters_download_search_keeps_full_query()
    {
        var parameters = SlashCommandRouting.ParseParameters("/download_search", "ubuntu 22 iso");

        Assert.NotNull(parameters);
        Assert.Equal("ubuntu 22 iso", parameters!["query"]);
    }
}