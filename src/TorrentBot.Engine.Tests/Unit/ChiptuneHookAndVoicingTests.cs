using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptuneHookAndVoicingTests
{
    [Fact]
    public void Repeated_transposition_independent_midi_hook_is_detected_as_chorus()
    {
        var notes = new List<NoteEvent>();
        // Four one-bar windows. Bars 2 and 4 repeat the same rhythmic/interval
        // hook at different absolute pitches; bars 1 and 3 are sparse verse material.
        AddHook(notes, 0, 60, 72, sparse: true);
        AddHook(notes, TempoMap.Ppq * 4, 62, 80, sparse: false);
        AddHook(notes, TempoMap.Ppq * 8, 57, 70, sparse: true);
        AddHook(notes, TempoMap.Ppq * 12, 67, 82, sparse: false);
        var song = new Song(notes, TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("midi_base64=AQ== chip=nes style=happy register=off format=wav");

        var planned = ArrangementPlanner.Plan(song, spec);

        Assert.Contains(planned.Notes, x => x.StartTick >= TempoMap.Ppq * 4 && x.StartTick < TempoMap.Ppq * 8 && x.Section == "chorus");
        Assert.Contains(planned.Notes, x => x.StartTick >= TempoMap.Ppq * 12 && x.Section == "chorus");
        Assert.Contains(planned.Notes, x => x.StartTick < TempoMap.Ppq * 4 && x.Section != "chorus");
    }

    [Fact]
    public void Auto_midi_chorus_brightens_supportive_program_used_as_main_lead()
    {
        var song = new Song([
            new NoteEvent(0, 480, 76, 110, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0, Program: 88,
                Section: "chorus", SectionIntensity: .95),
            new NoteEvent(480, 480, 79, 110, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0, Program: 88,
                Section: "chorus", SectionIntensity: .95)
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("midi_base64=AQ== chip=nes style=happy register=off format=wav");

        var planned = ArrangementPlanner.Plan(song, spec);

        Assert.All(planned.Notes, x => Assert.Equal("lead", x.Patch));
    }

    [Fact]
    public void Explicit_midi_lead_override_is_never_replaced_by_chorus_adaptation()
    {
        var song = new Song([
            new NoteEvent(0, 480, 76, 110, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0, Program: 88,
                Section: "chorus", SectionIntensity: .95)
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("midi_base64=AQ== chip=nes style=happy lead=brass register=off format=wav");

        var planned = ArrangementPlanner.Plan(song, spec);

        Assert.Equal("brass", Assert.Single(planned.Notes).Patch);
    }

    [Fact]
    public void Chorus_hook_can_replace_lower_priority_support_on_single_voice_target()
    {
        var song = new Song([
            new NoteEvent(0, 960, 55, 76, TrackRole.Harmony, SourceTrack: 0, SourceChannel: 0, Program: 88,
                Section: "verse", SectionIntensity: .45, Patch: "pad"),
            new NoteEvent(240, 480, 79, 116, TrackRole.CounterLead, SourceTrack: 1, SourceChannel: 1, Program: 81,
                Section: "chorus", SectionIntensity: .98, Patch: "bell")
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=pcspeaker fidelity=recognizable format=wav");

        var hardware = VoiceAllocator.Allocate(song, spec);
        var pad = Assert.Single(hardware.Notes, x => x.Role == TrackRole.Harmony);
        var hook = Assert.Single(hardware.Notes, x => x.Role == TrackRole.CounterLead);

        Assert.Equal(240, pad.DurationTick);
        Assert.Equal(240, hook.StartTick);
        Assert.Equal(0, hardware.DroppedNotes);
    }

    [Fact]
    public void Defining_chord_tones_are_considered_before_nonessential_extensions()
    {
        var song = new Song([
            new NoteEvent(0, 480, 48, 110, TrackRole.Bass, SourceTrack: 0, SourceChannel: 0, Program: 32),
            new NoteEvent(0, 480, 60, 82, TrackRole.Harmony, SourceTrack: 1, SourceChannel: 1, Program: 48), // root
            new NoteEvent(0, 480, 64, 82, TrackRole.Harmony, SourceTrack: 1, SourceChannel: 1, Program: 48), // third
            new NoteEvent(0, 480, 67, 82, TrackRole.Harmony, SourceTrack: 1, SourceChannel: 1, Program: 48), // fifth
            new NoteEvent(0, 480, 66, 112, TrackRole.Harmony, SourceTrack: 1, SourceChannel: 1, Program: 48)  // non-chord extension
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=c64_8580 fidelity=strict format=wav");

        var hardware = VoiceAllocator.Allocate(song, spec);
        var kept = hardware.Notes.Select(x => x.Pitch).ToHashSet();

        Assert.Contains(48, kept);
        Assert.True(kept.Contains(60) || kept.Contains(64) || kept.Contains(67), "At least one defining chord tone must survive constrained voicing.");
    }

    [Fact]
    public void Genesis_counterlead_prefers_psg_when_available()
    {
        var profile = ChipProfile.For("genesis");
        var note = new NoteEvent(0, 480, 76, 110, TrackRole.CounterLead, Patch: "bell");

        var candidates = profile.Candidates(note);

        Assert.Equal(6, candidates[0]);
        Assert.Equal(7, candidates[1]);
        Assert.Equal(8, candidates[2]);
    }

    private static void AddHook(List<NoteEvent> notes, long start, int basePitch, int velocity, bool sparse)
    {
        var intervals = sparse ? new[] { 0, 2, 1 } : new[] { 0, 4, 7, 4, 2, 4 };
        var positions = sparse ? new[] { 0, 8, 12 } : new[] { 0, 2, 4, 6, 10, 12 };
        for (var i = 0; i < intervals.Length; i++)
            notes.Add(new NoteEvent(start + positions[i] * TempoMap.Ppq / 4, TempoMap.Ppq / 2,
                basePitch + intervals[i], velocity, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0, Program: 80));
        if (!sparse)
        {
            notes.Add(new NoteEvent(start, TempoMap.Ppq * 4, basePitch - 24, 100, TrackRole.Bass,
                SourceTrack: 1, SourceChannel: 1, Program: 32));
            notes.Add(new NoteEvent(start, TempoMap.Ppq / 8, 36, 110, TrackRole.Drums, SourceTrack: 2, SourceChannel: 9));
            notes.Add(new NoteEvent(start + TempoMap.Ppq * 2, TempoMap.Ppq / 8, 38, 105, TrackRole.Drums, SourceTrack: 2, SourceChannel: 9));
        }
    }
}
