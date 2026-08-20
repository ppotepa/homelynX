using System.Text.Json;
using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptuneSectionInstrumentOverrideTests
{
    [Fact]
    public void Compact_instrument_map_accepts_section_role_keys()
    {
        var spec = ChiptuneParser.Parse("generate=song chip=nes style=happy bars=8 instruments=\"verse.lead:soft_lead,chorus.lead:brass,chorus.counter:bell\"");

        Assert.NotNull(spec.SectionInstruments);
        Assert.Equal("soft_lead", spec.SectionInstruments!["verse.lead"]);
        Assert.Equal("brass", spec.SectionInstruments["chorus.lead"]);
        Assert.Equal("bell", spec.SectionInstruments["chorus.counter"]);
    }

    [Fact]
    public void Direct_section_options_are_normalized_into_section_map()
    {
        var spec = ChiptuneParser.Parse("generate=song chip=snes style=happy bars=8 verse_lead=epiano chorus_lead=brass chorus_counter=flute");

        Assert.Equal("epiano", spec.SectionInstruments!["verse.lead"]);
        Assert.Equal("brass", spec.SectionInstruments["chorus.lead"]);
        Assert.Equal("flute", spec.SectionInstruments["chorus.counter"]);
    }

    [Fact]
    public void Section_override_wins_only_inside_matching_section()
    {
        var spec = ChiptuneParser.Parse("generate=song chip=nes style=happy bars=8 seed=42 lead=bell chorus_lead=brass");
        var planned = ArrangementPlanner.Plan(ChiptuneParser.Compose(spec), spec);

        Assert.All(planned.Notes.Where(x => x.Role == TrackRole.Lead && x.Section != "chorus"), x => Assert.Equal("bell", x.Patch));
        Assert.All(planned.Notes.Where(x => x.Role == TrackRole.Lead && x.Section == "chorus"), x => Assert.Equal("brass", x.Patch));
    }

    [Fact]
    public void Section_drum_override_can_change_only_chorus_percussion_patch()
    {
        var spec = ChiptuneParser.Parse("generate=song chip=snes style=happy bars=8 seed=42 chorus_drums=snare");
        var planned = ArrangementPlanner.Plan(ChiptuneParser.Compose(spec), spec);
        var chorusDrums = planned.Notes.Where(x => x.Role == TrackRole.Drums && x.Section == "chorus").ToArray();
        var otherDrums = planned.Notes.Where(x => x.Role == TrackRole.Drums && x.Section != "chorus").ToArray();

        Assert.NotEmpty(chorusDrums);
        Assert.All(chorusDrums, x => Assert.Equal("snare", x.Patch));
        Assert.Contains(otherDrums, x => x.Patch != "snare");
    }

    [Fact]
    public void Section_instrument_map_round_trips_through_session_json()
    {
        var spec = ChiptuneParser.Parse("generate=song chip=nes style=happy bars=8 instruments=\"verse.lead:soft_lead,chorus.lead:brass,chorus.counter:bell\"");

        var json = JsonSerializer.Serialize(spec);
        var restored = JsonSerializer.Deserialize<ChiptuneSpec>(json);

        Assert.NotNull(restored);
        Assert.NotNull(restored!.SectionInstruments);
        Assert.Equal("soft_lead", restored.SectionInstruments!["verse.lead"]);
        Assert.Equal("brass", restored.SectionInstruments["chorus.lead"]);
        Assert.Equal("bell", restored.SectionInstruments["chorus.counter"]);
    }

    [Fact]
    public void Invalid_section_or_patch_is_rejected_at_parse_time()
    {
        Assert.Throws<FormatException>(() => ChiptuneParser.Parse("generate=song instruments=\"bridge.lead:lead\""));
        Assert.Throws<FormatException>(() => ChiptuneParser.Parse("generate=song chorus_lead=not_a_patch"));
    }
}
