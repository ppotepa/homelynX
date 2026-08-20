using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptuneMeterAndRegisterRegressionTests
{
    [Fact]
    public void Entirely_chorus_midi_part_is_not_lifted_without_contrast_reference()
    {
        var spec = ChiptuneParser.Parse("midi_base64=AQ== chip=nes register=auto chorus_lift=12 format=wav");
        var song = new Song([
            new NoteEvent(0, 480, 60, 108, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0, Program: 80,
                Section: "chorus", SectionIntensity: .95),
            new NoteEvent(480, 480, 64, 108, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0, Program: 80,
                Section: "chorus", SectionIntensity: .95),
            new NoteEvent(960, 480, 67, 108, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0, Program: 80,
                Section: "chorus", SectionIntensity: .95)
        ], TempoMap.Fixed(120));

        var planned = ArrangementPlanner.Plan(song, spec).Notes.OrderBy(x => x.StartTick).ToArray();

        Assert.Equal([60, 64, 67], planned.Select(x => x.Pitch).ToArray());
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(6, 8)]
    public void Repeated_hook_detection_respects_non_four_four_bar_length(int numerator, int denominator)
    {
        var bar = TempoMap.Ppq * 3L;
        var notes = new List<NoteEvent>();
        AddSparseVerse(notes, 0, 60);
        AddHook(notes, bar, 62);
        AddSparseVerse(notes, bar * 2, 57);
        AddHook(notes, bar * 3, 67);
        var metadata = new MidiMetadata(
            new Dictionary<int, string> { [0] = "Main Melody", [1] = "Bass", [2] = "Drums" },
            [new TimeSignaturePoint(0, numerator, denominator)],
            []);
        var song = new Song(notes, TempoMap.Fixed(120), metadata);
        var spec = ChiptuneParser.Parse("midi_base64=AQ== chip=nes style=happy register=off format=wav");

        var planned = ArrangementPlanner.Plan(song, spec);
        var secondBar = planned.Notes.Where(x => x.StartTick >= bar && x.StartTick < bar * 2).ToArray();
        var fourthBar = planned.Notes.Where(x => x.StartTick >= bar * 3 && x.StartTick < bar * 4).ToArray();

        Assert.NotEmpty(secondBar);
        Assert.NotEmpty(fourthBar);
        Assert.All(secondBar, x => Assert.Equal("chorus", x.Section));
        Assert.All(fourthBar, x => Assert.Equal("chorus", x.Section));
    }

    private static void AddSparseVerse(List<NoteEvent> notes, long start, int pitch)
    {
        notes.Add(new NoteEvent(start, TempoMap.Ppq / 2, pitch, 76, TrackRole.Lead,
            SourceTrack: 0, SourceChannel: 0, Program: 80));
        notes.Add(new NoteEvent(start + TempoMap.Ppq * 2, TempoMap.Ppq / 2, pitch + 2, 72, TrackRole.Lead,
            SourceTrack: 0, SourceChannel: 0, Program: 80));
    }

    private static void AddHook(List<NoteEvent> notes, long start, int basePitch)
    {
        var intervals = new[] { 0, 4, 7, 4, 2, 4 };
        var positions = new[] { 0, 2, 4, 6, 8, 10 };
        for (var i = 0; i < intervals.Length; i++)
            notes.Add(new NoteEvent(start + positions[i] * TempoMap.Ppq / 4, TempoMap.Ppq / 2,
                basePitch + intervals[i], 108, TrackRole.Lead,
                SourceTrack: 0, SourceChannel: 0, Program: 80));
        notes.Add(new NoteEvent(start, TempoMap.Ppq * 3, basePitch - 24, 96, TrackRole.Bass,
            SourceTrack: 1, SourceChannel: 1, Program: 32));
        notes.Add(new NoteEvent(start, TempoMap.Ppq / 8, 36, 110, TrackRole.Drums,
            SourceTrack: 2, SourceChannel: 9));
        notes.Add(new NoteEvent(start + TempoMap.Ppq * 3 / 2, TempoMap.Ppq / 8, 38, 104, TrackRole.Drums,
            SourceTrack: 2, SourceChannel: 9));
    }
}
