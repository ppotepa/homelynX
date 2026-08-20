using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptuneGeneratedDefaultsTests
{
    [Fact]
    public void Song_defaults_to_eight_bars_and_style_specific_happy_tempo()
    {
        var spec = ChiptuneParser.Parse("generate=song style=happy");

        Assert.Equal(8, spec.Bars);
        Assert.Equal(156, spec.Bpm);
        Assert.False(spec.ChipExplicit);
        Assert.Equal("auto", spec.Instrument);
    }

    [Fact]
    public void Explicit_song_bars_and_bpm_override_style_defaults()
    {
        var spec = ChiptuneParser.Parse("generate=song style=happy bars=16 bpm=132 chip=nes");

        Assert.Equal(16, spec.Bars);
        Assert.Equal(132, spec.Bpm);
        Assert.True(spec.ChipExplicit);
        Assert.Equal("nes", spec.Chip);
    }

    [Theory]
    [InlineData("racing", 174)]
    [InlineData("boss", 150)]
    [InlineData("jrpg", 138)]
    [InlineData("dungeon", 112)]
    [InlineData("minimal", 108)]
    public void Generated_styles_choose_musical_default_tempos(string style, int expectedBpm)
    {
        var spec = ChiptuneParser.Parse($"generate=melody style={style}");
        Assert.Equal(expectedBpm, spec.Bpm);
    }

    [Fact]
    public void Long_song_has_bridge_and_final_chorus()
    {
        var spec = ChiptuneParser.Parse("generate=song chip=nes style=happy bars=16 seed=42 format=wav");
        var song = ChiptuneParser.Compose(spec);

        Assert.Contains(song.Notes, x => x.Section == "bridge" && x.Role == TrackRole.Lead);
        Assert.Contains(song.Notes, x => x.Section == "bridge" && x.Role == TrackRole.Harmony);
        Assert.Contains(song.Notes, x => x.Section == "chorus" && x.SectionIntensity >= .99);
        Assert.Contains(song.Notes, x => x.Section == "outro");
    }

    [Fact]
    public void Generated_chorus_and_bridge_bass_stay_in_bass_register_before_planning()
    {
        var spec = ChiptuneParser.Parse("generate=song chip=nes style=happy bars=16 seed=42 register=off format=wav");
        var bass = ChiptuneParser.Compose(spec).Notes.Where(x => x.Role == TrackRole.Bass).ToArray();

        Assert.NotEmpty(bass);
        Assert.All(bass, note => Assert.InRange(note.Pitch, 28, 52));
    }
}
