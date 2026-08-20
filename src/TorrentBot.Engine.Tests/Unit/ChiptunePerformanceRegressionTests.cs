using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptunePerformanceRegressionTests
{
    [Fact]
    public void Sustain_without_pedal_up_extends_to_track_end()
    {
        var midi = Midi([
            0, 0xB0, 64, 127,
            0, 0x90, 60, 100,
            30, 0x80, 60, 0,
            30, 0xFF, 0x2F, 0
        ]);
        var spec = ChiptuneParser.Parse($"midi_base64={Convert.ToBase64String(midi)}");

        var note = Assert.Single(ChiptuneParser.Compose(spec).Notes);

        Assert.Equal(120L, note.DurationTick);
    }

    [Fact]
    public void Pitch_bend_during_sustain_is_preserved_after_key_release()
    {
        var midi = Midi([
            0, 0xB0, 64, 127,
            0, 0x90, 60, 100,
            30, 0x80, 60, 0,
            15, 0xE0, 0, 96,
            15, 0xB0, 64, 0,
            0, 0xFF, 0x2F, 0
        ]);
        var spec = ChiptuneParser.Parse($"midi_base64={Convert.ToBase64String(midi)}");

        var note = Assert.Single(ChiptuneParser.Compose(spec).Notes);

        Assert.Equal(120L, note.DurationTick);
        Assert.Contains(new PitchBendPoint(90, 12288), note.PitchBends!);
    }

    [Fact]
    public void Polyphonic_aftertouch_only_modulates_its_own_key()
    {
        var midi = Midi([
            0, 0x90, 60, 100,
            0, 0x90, 64, 100,
            30, 0xA0, 60, 80,
            30, 0x80, 60, 0,
            0, 0x80, 64, 0,
            0, 0xFF, 0x2F, 0
        ]);
        var spec = ChiptuneParser.Parse($"midi_base64={Convert.ToBase64String(midi)}");
        var notes = ChiptuneParser.Compose(spec).Notes.OrderBy(x => x.Pitch).ToArray();

        Assert.Equal(2, notes.Length);
        Assert.Contains(notes[0].ControllerChanges!, x => x.Aftertouch == 80);
        Assert.True(notes[1].ControllerChanges is null || notes[1].ControllerChanges.All(x => x.Aftertouch == 0));
    }

    [Fact]
    public void Polyphonic_source_lanes_keep_hardware_voice_continuity()
    {
        var song = new Song([
            new NoteEvent(0, 480, 60, 100, TrackRole.Harmony, SourceTrack: 0, SourceChannel: 0, Program: 0),
            new NoteEvent(0, 480, 67, 100, TrackRole.Harmony, SourceTrack: 0, SourceChannel: 0, Program: 0),
            new NoteEvent(480, 480, 62, 100, TrackRole.Harmony, SourceTrack: 0, SourceChannel: 0, Program: 0),
            new NoteEvent(480, 480, 69, 100, TrackRole.Harmony, SourceTrack: 0, SourceChannel: 0, Program: 0)
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=nes fidelity=recognizable format=wav");

        var notes = VoiceAllocator.Allocate(song, spec).Notes;
        var low = notes.Where(x => x.Pitch is 60 or 62).OrderBy(x => x.StartTick).ToArray();
        var high = notes.Where(x => x.Pitch is 67 or 69).OrderBy(x => x.StartTick).ToArray();

        Assert.Equal(2, low.Length);
        Assert.Equal(2, high.Length);
        Assert.Equal(low[0].Voice, low[1].Voice);
        Assert.Equal(high[0].Voice, high[1].Voice);
        Assert.NotEqual(low[0].Voice, high[0].Voice);
    }

    private static byte[] Midi(byte[] track)
    {
        var result = new List<byte>
        {
            (byte)'M',(byte)'T',(byte)'h',(byte)'d', 0,0,0,6,
            0,0, 0,1, 1,0xE0,
            (byte)'M',(byte)'T',(byte)'r',(byte)'k',
            (byte)(track.Length >> 24), (byte)(track.Length >> 16), (byte)(track.Length >> 8), (byte)track.Length
        };
        result.AddRange(track);
        return result.ToArray();
    }
}
