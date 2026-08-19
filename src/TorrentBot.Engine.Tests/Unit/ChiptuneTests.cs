using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptuneTests
{
    [Fact]
    public void Degrees_respect_key_scale_and_octave()
    {
        var spec = ChiptuneParser.Parse("degrees=\"1/8 2/8 3/8 8/4\" key=D scale=minor octave=4 format=wav");
        var song = ChiptuneParser.Compose(spec);
        Assert.Equal([62, 64, 65, 74], song.Notes.Select(x => x.Pitch));
    }

    [Fact]
    public void Generator_is_deterministic_for_seed()
    {
        var a = ChiptuneParser.Compose(ChiptuneParser.Parse("generate=riff key=E scale=phrygian seed=42 bars=2 format=wav"));
        var b = ChiptuneParser.Compose(ChiptuneParser.Parse("generate=riff key=E scale=phrygian seed=42 bars=2 format=wav"));
        var c = ChiptuneParser.Compose(ChiptuneParser.Parse("generate=riff key=E scale=phrygian seed=43 bars=2 format=wav"));
        Assert.Equal(a.Notes, b.Notes);
        Assert.NotEqual(a.Notes.Where(x=>x.Role==TrackRole.Lead).Select(x=>x.Pitch), c.Notes.Where(x=>x.Role==TrackRole.Lead).Select(x=>x.Pitch));
    }

    [Fact]
    public void Tempo_map_integrates_each_segment()
    {
        var map = new TempoMap([new TempoPoint(0, 500_000), new TempoPoint(960, 1_000_000)]);
        Assert.Equal(1.5, map.TickToSeconds(1920), 6);
    }

    [Fact]
    public void Voice_allocator_uses_hardware_roles()
    {
        var spec = ChiptuneParser.Parse("generate=riff chip=nes bars=1 format=wav");
        var hardware = VoiceAllocator.Allocate(ChiptuneParser.Compose(spec), spec);
        Assert.All(hardware.Notes.Where(x=>x.Role==TrackRole.Bass), x=>Assert.Equal(2,x.Voice));
        Assert.All(hardware.Notes.Where(x=>x.Role==TrackRole.Drums), x=>Assert.Equal(3,x.Voice));
    }

    [Theory]
    [InlineData("chip=c64", "C64/SID")]
    [InlineData("chip=unknown", "Unknown chip")]
    [InlineData("format=xyz", "Unknown format")]
    public void Invalid_options_fail_before_render(string option, string expected)
    {
        var ex = Assert.Throws<FormatException>(()=>ChiptuneParser.Parse($"notes=C4/4 {option}"));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Managed_renderer_returns_stereo_44100_wav()
    {
        var spec = ChiptuneParser.Parse("notes=\"C4/8 E4/8\" chip=gameboy format=wav");
        var bytes = ManagedChipRenderer.Render(VoiceAllocator.Allocate(ChiptuneParser.Compose(spec), spec));
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes,0,4));
        Assert.Equal(2, BitConverter.ToInt16(bytes,22));
        Assert.Equal(44_100, BitConverter.ToInt32(bytes,24));
        Assert.True(bytes.Length > 44);
    }
}
