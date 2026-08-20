using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptuneArrangementRegressionTests
{
    [Fact]
    public void Recognizable_mode_prefers_counter_melody_over_long_pad()
    {
        var song = new Song(
        [
            new NoteEvent(0, 960, 55, 86, TrackRole.Harmony,
                SourceTrack: 0, SourceChannel: 0, Program: 88),
            new NoteEvent(240, 480, 72, 112, TrackRole.Harmony,
                SourceTrack: 1, SourceChannel: 1, Program: 80)
        ], TempoMap.Fixed(120), new MidiMetadata(
            new Dictionary<int, string> { [0] = "Ambient Pad", [1] = "Counter Melody" },
            [], []));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=pcspeaker fidelity=recognizable format=wav");

        var hardware = VoiceAllocator.Allocate(song, spec);
        var pad = Assert.Single(hardware.Notes, x => x.Program == 88);
        var melody = Assert.Single(hardware.Notes, x => x.Program == 80);

        Assert.Equal(240, pad.DurationTick);
        Assert.Equal(240, melody.StartTick);
        Assert.Equal(0, hardware.DroppedNotes);
        Assert.True(hardware.RevoicedNotes >= 1);
    }

    [Fact]
    public void Auto_rank_prefers_high_capacity_timbre_target_for_dense_multitrack_MIDI()
    {
        var midiStub = new byte[]
        {
            (byte)'M',(byte)'T',(byte)'h',(byte)'d',0,0,0,6,0,0,0,1,1,0xE0,
            (byte)'M',(byte)'T',(byte)'r',(byte)'k',0,0,0,13,
            0,0x90,60,100,0x83,0x60,0x80,60,0,0,0xFF,0x2F,0
        };
        var spec = ChiptuneParser.Parse("midi_base64=" + Convert.ToBase64String(midiStub));
        var notes = Enumerable.Range(0, 8)
            .Select(i => new NoteEvent(0, 960, 48 + i * 3, 100, TrackRole.Harmony,
                SourceTrack: i, SourceChannel: i, Program: 40 + i))
            .ToArray();
        var song = new Song(notes, TempoMap.Fixed(120));

        var ranking = AutoProfileResolver.Rank(spec, song);

        Assert.NotEmpty(ranking);
        Assert.Equal("snes", ranking[0].Chip);
        Assert.True(ranking[0].Score > ranking.Single(x => x.Chip == "nes").Score);
        Assert.True(ranking[0].Score > ranking.Single(x => x.Chip == "gbc").Score);
    }
}
