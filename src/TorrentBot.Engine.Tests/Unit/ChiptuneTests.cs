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

    [Fact]
    public void Voice_allocator_prevents_old_note_off_from_cutting_a_new_note()
    {
        var song = new Song(
        [
            new NoteEvent(0, 960, 60, 100, TrackRole.Lead),
            new NoteEvent(240, 960, 64, 100, TrackRole.Lead)
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=nes");

        var notes = VoiceAllocator.Allocate(song, spec).Notes.OrderBy(x => x.StartTick).ToArray();

        Assert.Equal(240, notes[0].DurationTick);
        Assert.Equal(960, notes[1].DurationTick);
    }

    [Theory]
    [InlineData("chip=unknown", "Unknown chip")]
    [InlineData("format=xyz", "Unknown format")]
    public void Invalid_options_fail_before_render(string option, string expected)
    {
        var ex = Assert.Throws<FormatException>(()=>ChiptuneParser.Parse($"notes=C4/4 {option}"));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Fidelity_policy_is_parsed_and_strict_reports_dropped_notes()
    {
        var spec = ChiptuneParser.Parse("notes=\"[C4,E4,G4]/4\" chip=pcspeaker fidelity=strict");
        var hardware = VoiceAllocator.Allocate(ChiptuneParser.Compose(spec), spec);
        Assert.Equal("strict", spec.Fidelity);
        Assert.True(hardware.DroppedNotes >= 1);
    }

    [Fact]
    public void Fidelity_preserve_arpeggiates_while_balanced_uses_voice_stealing()
    {
        var song = new Song(
        [
            new NoteEvent(0, 960, 60, 100, TrackRole.Lead),
            new NoteEvent(0, 960, 64, 100, TrackRole.Lead),
            new NoteEvent(0, 960, 67, 100, TrackRole.Lead)
        ], TempoMap.Fixed(120));
        var preserve = ChiptuneParser.Parse("notes=C4/4 chip=pcspeaker fidelity=preserve");
        var balanced = preserve with { Fidelity = "balanced" };

        var preserved = VoiceAllocator.Allocate(song, preserve);
        var balancedResult = VoiceAllocator.Allocate(song, balanced);

        Assert.True(preserved.ArpeggiatedNotes > 0);
        Assert.Equal(0, preserved.DroppedNotes);
        Assert.True(balancedResult.RevoicedNotes > 0);
        Assert.Equal(0, balancedResult.ArpeggiatedNotes);
    }

    [Theory]
    [InlineData("gb")]
    [InlineData("gbc")]
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

    [Theory]
    [InlineData("gameboy", "gb")]
    [InlineData("dmg", "gb")]
    [InlineData("gameboy_color", "gbc")]
    public void GameBoy_aliases_resolve_to_explicit_profiles(string input, string expected)
    {
        Assert.Equal(expected, ChiptuneParser.Parse($"notes=C4/4 chip={input}").Chip);
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
    public void Midi_import_preserves_fine_rhythm_and_single_track_as_lead()
    {
        var midi = new byte[]
        {
            (byte)'M',(byte)'T',(byte)'h',(byte)'d', 0,0,0,6, 0,0, 0,1, 1,0xE0,
            (byte)'M',(byte)'T',(byte)'r',(byte)'k', 0,0,0,20,
            0,0x90,60,100, 30,0x80,60,0,
            15,0x90,62,100, 45,0x80,62,0,
            0,0xFF,0x2F,0
        };
        var spec = ChiptuneParser.Parse($"midi_base64={Convert.ToBase64String(midi)}");
        var song = ChiptuneParser.Compose(spec);

        Assert.Equal("off", spec.Quantize);
        Assert.Equal([0L, 90L], song.Notes.Select(x => x.StartTick));
        Assert.Equal([60L, 90L], song.Notes.Select(x => x.DurationTick));
        Assert.All(song.Notes, note => Assert.Equal(TrackRole.Lead, note.Role));
    }

    [Fact]
    public void Midi_import_honors_program_and_sustain_pedal()
    {
        var midi = new byte[]
        {
            (byte)'M',(byte)'T',(byte)'h',(byte)'d', 0,0,0,6, 0,0, 0,1, 1,0xE0,
            (byte)'M',(byte)'T',(byte)'r',(byte)'k', 0,0,0,23,
            0,0xC0,32, 0,0xB0,64,127, 0,0x90,60,100,
            30,0x80,60,0, 30,0xB0,64,0, 0,0xFF,0x2F,0
        };
        var spec = ChiptuneParser.Parse($"midi_base64={Convert.ToBase64String(midi)}");
        var note = Assert.Single(ChiptuneParser.Compose(spec).Notes);

        Assert.Equal(32, note.Program);
        Assert.Equal(120, note.DurationTick);
    }

    [Fact]
    public void Allocator_keeps_program_and_percussion_patch_identity_separate_from_voice()
    {
        var song = new Song(
        [
            new NoteEvent(0, 240, 60, 100, TrackRole.Lead, Program: 0),
            new NoteEvent(240, 240, 64, 100, TrackRole.Lead, Program: 32),
            new NoteEvent(0, 120, 36, 110, TrackRole.Drums),
            new NoteEvent(120, 120, 42, 90, TrackRole.Drums)
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=nes format=wav");

        var hardware = VoiceAllocator.Allocate(song, spec).Notes.OrderBy(x => x.StartTick).ThenBy(x => x.Voice).ToArray();

        Assert.Contains(hardware, x => x.Instrument == "bass" && x.InstrumentId == 42);
        Assert.Contains(hardware, x => x.Instrument == "kick" && x.InstrumentId == 200);
        Assert.Contains(hardware, x => x.Instrument == "hat" && x.InstrumentId == 202);
        Assert.NotEqual(hardware.Single(x => x.Instrument == "kick").Voice, hardware.Single(x => x.Instrument == "kick").InstrumentId);
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
