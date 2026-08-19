using TorrentBot.Adapters.Telegram;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class TelegramInvocationAdapterTests
{
    [Fact]
    public void Bare_media_url_with_options_preserves_clip_parameters()
    {
        var adapter = new TelegramInvocationAdapter();
        var invocation = adapter.ToInvocation(
            new TelegramUpdate(1, "user", "https://www.youtube.com/watch?v=whQQpwwvSh4 mp4 720 clip 1:00 1:10"),
            new UserContext("user", [], "default"));

        Assert.True(invocation.IsExplicit);
        Assert.Equal("download.start_media", invocation.CapabilityName);
        Assert.Equal("mp4", invocation.Parameters!["format"]);
        Assert.Equal("720", invocation.Parameters["quality"]);
        Assert.Equal("1:00", invocation.Parameters["clipStart"]);
        Assert.Equal("1:10", invocation.Parameters["clipEnd"]);
    }

    [Fact]
    public void YouTube_short_with_options_is_a_media_download()
    {
        var adapter = new TelegramInvocationAdapter();
        var invocation = adapter.ToInvocation(
            new TelegramUpdate(1, "user", "https://www.youtube.com/shorts/whQQpwwvSh4 mp4 720 clip 0:01 0:03"),
            new UserContext("user", [], "default"));

        Assert.Equal("download.start_media", invocation.CapabilityName);
        Assert.Equal("0:01", invocation.Parameters!["clipStart"]);
        Assert.Equal("0:03", invocation.Parameters["clipEnd"]);
    }
}
