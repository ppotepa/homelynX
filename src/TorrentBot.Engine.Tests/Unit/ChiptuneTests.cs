using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptuneTests
{
    [Fact]
    public void Default_format_is_mp3()
    {
        var spec = ChiptuneParser.Parse("notes=\"C4/4\"");
        Assert.Equal("mp3", spec.Format);
    }

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
    [InlineData("chip=unknown", "Unknown chip")]
    [InlineData("format=xyz", "Unknown format")]
    public void Invalid_options_fail_before_render(string option, string expected)
    {
        var ex = Assert.Throws<FormatException>(()=>ChiptuneParser.Parse($"notes=C4/4 {option}"));
        Assert.Contains(expected, ex.Message);
    }

    [Theory]
    [InlineData("c64_6581")]
    [InlineData("c64_8580")]
    [InlineData("genesis")]
    [InlineData("pce")]
    [InlineData("atari2600")]
    [InlineData("pokey")]
    [InlineData("pcspeaker")]
    [InlineData("zx_spectrum")]
    public void Extended_hardware_profiles_parse_and_allocate(string chip)
    {
        var spec = ChiptuneParser.Parse($"generate=song chip={chip} bars=1 format=wav");
        var hardware = VoiceAllocator.Allocate(ChiptuneParser.Compose(spec), spec);
        Assert.NotEmpty(hardware.Notes);
        Assert.Equal(chip, hardware.Chip);
    }

    [Fact]
    public void Song_generator_accepts_progression_and_extended_sources()
    {
        var spec = ChiptuneParser.Parse("generate=song key=D scale=minor progression=\"i VI III VII\" bars=2 wave=saw format=wav");
        var song = ChiptuneParser.Compose(spec);
        Assert.Contains(song.Notes, note => note.Role == TrackRole.Bass);
        Assert.Equal("saw", spec.Wave);
    }

    [Fact]
    public void Midi_importer_accepts_rmid_container()
    {
        var midi = new byte[]
        {
            (byte)'M',(byte)'T',(byte)'h',(byte)'d', 0,0,0,6, 0,0, 0,1, 1,0xE0,
            (byte)'M',(byte)'T',(byte)'r',(byte)'k', 0,0,0,13,
            0,0x90,60,100, 0x83,0x60,0x80,60,0, 0,0xFF,0x2F,0
        };
        var rmid = new List<byte> { (byte)'R',(byte)'I',(byte)'F',(byte)'F', 0,0,0,0, (byte)'R',(byte)'M',(byte)'I',(byte)'D', (byte)'d',(byte)'a',(byte)'t',(byte)'a' };
        rmid.AddRange(BitConverter.GetBytes(midi.Length));
        rmid.AddRange(midi);
        var riffSize = BitConverter.GetBytes(rmid.Count - 8);
        for (var i = 0; i < 4; i++) rmid[4 + i] = riffSize[i];
        var spec = ChiptuneParser.Parse($"midi_base64={Convert.ToBase64String(rmid.ToArray())} format=wav");
        Assert.NotEmpty(ChiptuneParser.Compose(spec).Notes);
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
