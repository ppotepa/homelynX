using TorrentBot.Engine;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class SlashCommandRoutingTests
{
    [Theory]
    [InlineData("/download_search", "torrent.search")]
    [InlineData("/list", "system.help")]
    [InlineData("/commands", "system.help")]
    [InlineData("/download_media", "download.start_media")]
    public void ResolveCapabilityOverride_maps_shared_commands(string command, string capability) =>
        Assert.Equal(capability, SlashCommandRouting.ResolveCapabilityOverride(command));

    [Fact]
    public void ParseParameters_download_search_keeps_full_query()
    {
        var parameters = SlashCommandRouting.ParseParameters("/download_search", "ubuntu 22 iso");

        Assert.NotNull(parameters);
        Assert.Equal("ubuntu 22 iso", parameters!["query"]);
    }

    [Fact]
    public void ParseParameters_download_media_parses_format_and_quality()
    {
        var parameters = SlashCommandRouting.ParseParameters(
            "/download_media",
            "https://youtu.be/example mp3 128k");

        Assert.NotNull(parameters);
        Assert.Equal("media", parameters!["provider"]);
        Assert.Equal("mp3", parameters["format"]);
        Assert.Equal("128", parameters["quality"]);
    }

    [Theory]
    [InlineData("https://youtu.be/example mp4 720 clip 00:22 00:33", "00:22", "00:33")]
    [InlineData("https://youtu.be/example mp4 720 00:22-00:33", "00:22", "00:33")]
    [InlineData("https://youtu.be/example mp4 720 clip(22, 33)", "22", "33")]
    public void ParseParameters_download_media_parses_clip_range(string command, string expectedStart, string expectedEnd)
    {
        var parameters = SlashCommandRouting.ParseParameters("/download_media", command);

        Assert.NotNull(parameters);
        Assert.Equal(expectedStart, parameters!["clipStart"]);
        Assert.Equal(expectedEnd, parameters["clipEnd"]);
    }

    [Fact]
    public void ParseParameters_download_media_parses_standalone_subtitles()
    {
        var parameters = SlashCommandRouting.ParseParameters(
            "/download_media",
            "https://youtu.be/example subtitles en pl auto");

        Assert.NotNull(parameters);
        Assert.Equal("subtitles", parameters!["format"]);
        Assert.Equal("en,pl,auto", parameters["subtitles"]);
    }
}
