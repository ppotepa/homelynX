using System.Reflection;
using TorrentBot.Adapters.Telegram.Sdk;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptuneTelegramAttachmentRoutingTests
{
    [Theory]
    [InlineData("/chiptune generate=song chip=nes style=happy", false)]
    [InlineData("/chiptune notes=\"C4/4 E4/4\" chip=gbc", false)]
    [InlineData("/chiptune degrees=\"1/4 3/4 5/4\" key=D", false)]
    [InlineData("/chiptune midi_base64=AAAA chip=nes", false)]
    [InlineData("/chiptune instruments chip=nes", false)]
    [InlineData("/chiptune", true)]
    [InlineData("/chiptune chip=nes fidelity=recognizable", true)]
    [InlineData("/chiptune inspect chip=nes", true)]
    public void Attached_midi_is_used_only_when_text_does_not_define_a_source(string text, bool expected)
    {
        var method = typeof(TelegramProductionAdapter).GetMethod(
            "ShouldAttachChiptuneMidi",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(expected, (bool)method!.Invoke(null, [text])!);
    }

    [Fact]
    public void Dbz_attachment_with_generate_song_does_not_become_a_second_source()
    {
        const string text = "/chiptune generate=song chip=nes style=happy key=D scale=major seed=42";
        var method = typeof(TelegramProductionAdapter).GetMethod(
            "ShouldAttachChiptuneMidi",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.False((bool)method!.Invoke(null, [text])!);
    }
}
