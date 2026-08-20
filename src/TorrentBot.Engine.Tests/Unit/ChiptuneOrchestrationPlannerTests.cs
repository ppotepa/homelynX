using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptuneOrchestrationPlannerTests
{
    [Fact]
    public void Parser_accepts_melody_and_role_specific_instrument_overrides()
    {
        var spec = ChiptuneParser.Parse("generate=melody chip=nes style=happy instruments=\"lead:soft_lead,counter:bell,bass:bass,arp:pluck\" harmony=strings chorus_lift=12");

        Assert.Equal("melody", spec.Generate);
        Assert.Equal("auto", spec.Instrument);
        Assert.Equal("soft_lead", spec.LeadInstrument);
        Assert.Equal("bell", spec.CounterInstrument);
        Assert.Equal("bass", spec.BassInstrument);
        Assert.Equal("strings", spec.HarmonyInstrument);
        Assert.Equal("pluck", spec.ArpInstrument);
        Assert.Equal(12, spec.ChorusLift);
    }

    [Fact]
    public void Happy_song_has_real_sections_counterline_and_higher_chorus_register()
    {
        var spec = ChiptuneParser.Parse("generate=song chip=nes style=happy key=D scale=major bpm=164 bars=8 seed=42 format=wav");
        var raw = ChiptuneParser.Compose(spec);
        var planned = ArrangementPlanner.Plan(raw, spec);

        Assert.Contains(planned.Notes, x => x.Section == "verse" && x.Role == TrackRole.Lead);
        Assert.Contains(planned.Notes, x => x.Section == "chorus" && x.Role == TrackRole.Lead);
        Assert.Contains(planned.Notes, x => x.Section == "chorus" && x.Role == TrackRole.CounterLead);
        Assert.Contains(planned.Notes, x => x.Role == TrackRole.Bass);
        Assert.Contains(planned.Notes, x => x.Role == TrackRole.Arp);
        Assert.Contains(planned.Notes, x => x.Role == TrackRole.Drums);

        var verse = planned.Notes.Where(x => x.Section == "verse" && x.Role == TrackRole.Lead).Select(x => x.Pitch).Order().ToArray();
        var chorus = planned.Notes.Where(x => x.Section == "chorus" && x.Role == TrackRole.Lead).Select(x => x.Pitch).Order().ToArray();
        Assert.NotEmpty(verse);
        Assert.NotEmpty(chorus);
        Assert.True(chorus[chorus.Length / 2] > verse[verse.Length / 2], "Chorus lead should sit above the verse register.");
        Assert.True(planned.Notes.Where(x => x.Section == "chorus").Average(x => x.SectionIntensity) >
                    planned.Notes.Where(x => x.Section == "verse").Average(x => x.SectionIntensity));
    }

    [Fact]
    public void Happy_nes_auto_palette_assigns_distinct_musical_roles()
    {
        var spec = ChiptuneParser.Parse("generate=song chip=nes style=happy bars=8 seed=7 format=wav");
        var planned = ArrangementPlanner.Plan(ChiptuneParser.Compose(spec), spec);

        Assert.Contains(planned.Notes, x => x.Role == TrackRole.Lead && x.Patch == "lead");
        Assert.Contains(planned.Notes, x => x.Role == TrackRole.CounterLead && x.Patch == "bell");
        Assert.All(planned.Notes.Where(x => x.Role == TrackRole.Bass), x => Assert.Equal("bass", x.Patch));
        Assert.All(planned.Notes.Where(x => x.Role == TrackRole.Arp), x => Assert.Equal("pluck", x.Patch));
        Assert.Contains(planned.Notes, x => x.Role == TrackRole.Drums && x.Patch is "kick" or "snare" or "hat" or "open_hat" or "crash");
    }

    [Fact]
    public void Explicit_role_instruments_override_auto_palette_without_affecting_other_roles()
    {
        var spec = ChiptuneParser.Parse("generate=song chip=snes style=happy bars=8 seed=11 lead=brass counter=flute arp=bell bass=bass harmony=organ format=wav");
        var planned = ArrangementPlanner.Plan(ChiptuneParser.Compose(spec), spec);

        Assert.All(planned.Notes.Where(x => x.Role == TrackRole.Lead), x => Assert.Equal("brass", x.Patch));
        Assert.All(planned.Notes.Where(x => x.Role == TrackRole.CounterLead), x => Assert.Equal("flute", x.Patch));
        Assert.All(planned.Notes.Where(x => x.Role == TrackRole.Arp), x => Assert.Equal("bell", x.Patch));
        Assert.All(planned.Notes.Where(x => x.Role == TrackRole.Bass), x => Assert.Equal("bass", x.Patch));
        Assert.All(planned.Notes.Where(x => x.Role == TrackRole.Harmony), x => Assert.Equal("organ", x.Patch));
    }

    [Fact]
    public void Register_off_preserves_generated_pitch_exactly()
    {
        var spec = ChiptuneParser.Parse("generate=song chip=nes style=happy bars=8 seed=9 register=off format=wav");
        var raw = ChiptuneParser.Compose(spec);
        var planned = ArrangementPlanner.Plan(raw, spec);

        Assert.Equal(raw.Notes.Select(x => x.Pitch), planned.Notes.Select(x => x.Pitch));
    }

    [Fact]
    public void Midi_counter_melody_is_promoted_above_generic_harmony()
    {
        var spec = ChiptuneParser.Parse("midi_base64=AQ== chip=nes format=wav");
        var song = new Song([
            new NoteEvent(0, 480, 72, 110, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0, Program: 80),
            new NoteEvent(480, 480, 74, 110, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0, Program: 80),
            new NoteEvent(0, 480, 67, 96, TrackRole.Harmony, SourceTrack: 1, SourceChannel: 1, Program: 81),
            new NoteEvent(480, 480, 69, 96, TrackRole.Harmony, SourceTrack: 1, SourceChannel: 1, Program: 81),
            new NoteEvent(0, 960, 55, 70, TrackRole.Harmony, SourceTrack: 2, SourceChannel: 2, Program: 88)
        ], TempoMap.Fixed(120), new MidiMetadata(
            new Dictionary<int, string> { [0] = "Main Melody", [1] = "Counter Melody", [2] = "Warm Pad" }, [], []));

        var planned = ArrangementPlanner.Plan(song, spec);

        Assert.All(planned.Notes.Where(x => x.SourceTrack == 1), x => Assert.Equal(TrackRole.CounterLead, x.Role));
        Assert.All(planned.Notes.Where(x => x.SourceTrack == 2), x => Assert.Equal(TrackRole.Harmony, x.Role));
    }

    [Fact]
    public void Auto_register_lifts_low_midi_chorus_without_changing_intervals()
    {
        var spec = ChiptuneParser.Parse("midi_base64=AQ== chip=nes register=auto chorus_lift=12 format=wav");
        var song = new Song([
            new NoteEvent(0, 480, 36, 105, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0, Program: 80, Section: "chorus", SectionIntensity: .95),
            new NoteEvent(480, 480, 40, 105, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0, Program: 80, Section: "chorus", SectionIntensity: .95)
        ], TempoMap.Fixed(120));

        var planned = ArrangementPlanner.Plan(song, spec).Notes.OrderBy(x => x.StartTick).ToArray();

        Assert.True(planned[0].Pitch >= 60);
        Assert.Equal(4, planned[1].Pitch - planned[0].Pitch);
    }
}
