using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptunePatchRegressionTests
{
    [Fact]
    public void Arp_alias_uses_pluck_but_explicit_patch_is_preserved()
    {
        var arp = new Song([new NoteEvent(0, 240, 72, 100, TrackRole.Arp)], TempoMap.Fixed(120));
        var aliasSpec = ChiptuneParser.Parse("notes=C5/4 chip=gbc instrument=arp format=wav");
        var bellSpec = aliasSpec with { Instrument = "bell" };

        Assert.Equal("pluck", Assert.Single(VoiceAllocator.Allocate(arp, aliasSpec).Notes).Instrument);
        Assert.Equal("bell", Assert.Single(VoiceAllocator.Allocate(arp, bellSpec).Notes).Instrument);
    }

    [Fact]
    public void Genesis_tonal_drums_prefer_fm6_and_noise_drums_prefer_psg_noise()
    {
        var song = new Song([
            new NoteEvent(0, 120, 36, 110, TrackRole.Drums),
            new NoteEvent(240, 120, 42, 100, TrackRole.Drums)
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=genesis format=wav");
        var notes = VoiceAllocator.Allocate(song, spec).Notes.OrderBy(x => x.StartTick).ToArray();

        Assert.Equal(5, notes[0].Voice);
        Assert.Equal("fm", notes[0].VoiceClass);
        Assert.Equal("kick", notes[0].Instrument);
        Assert.Equal(9, notes[1].Voice);
        Assert.Equal("noise", notes[1].VoiceClass);
        Assert.Equal("hat", notes[1].Instrument);
    }

    [Fact]
    public void Pce_kick_remains_tonal_wavetable()
    {
        var song = new Song([new NoteEvent(0, 120, 36, 110, TrackRole.Drums)], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=pce format=wav");
        var note = Assert.Single(VoiceAllocator.Allocate(song, spec).Notes);

        Assert.Contains(note.Voice, new[] { 4, 5 });
        Assert.Equal("wavetable", note.VoiceClass);
        Assert.Equal("kick", note.Instrument);
    }
}
