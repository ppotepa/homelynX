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
    public void Midi_defaults_to_recognizable_and_auto_chip_when_chip_is_omitted()
    {
        var spec = ChiptuneParser.Parse("midi_base64=" + Convert.ToBase64String(new byte[]
        {
            (byte)'M',(byte)'T',(byte)'h',(byte)'d',0,0,0,6,0,0,0,1,1,0xE0,
            (byte)'M',(byte)'T',(byte)'r',(byte)'k',0,0,0,13,
            0,0x90,60,100,0x83,0x60,0x80,60,0,0,0xFF,0x2F,0
        }));
        var resolved = AutoProfileResolver.Resolve(spec, ChiptuneParser.Compose(spec));

        Assert.False(spec.ChipExplicit);
        Assert.Equal("recognizable", spec.Fidelity);
        Assert.Equal("gbc", resolved.Chip);
    }

    [Fact]
    public void Chip_profiles_expose_real_dynamic_voice_pools()
    {
        var snes = ChipProfile.For("snes");
        var zx = ChipProfile.For("zx_spectrum");

        Assert.Equal(8, snes.Voices.Count);
        Assert.Equal(6, zx.Voices.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], snes.Candidates(new NoteEvent(0, 120, 60, 100, TrackRole.Harmony)));
        Assert.Equal([0, 1, 2, 3, 4, 5], zx.Candidates(new NoteEvent(0, 120, 60, 100, TrackRole.Lead)));
    }

    [Fact]
    public void Snes_uses_available_sample_pool_before_reducing_a_chord()
    {
        var song = new Song(Enumerable.Range(0, 8)
            .Select(i => new NoteEvent(0, 960, 48 + i * 2, 100, TrackRole.Harmony, SourceTrack: i, SourceChannel: i))
            .ToArray(), TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=snes fidelity=recognizable format=wav");
        var hardware = VoiceAllocator.Allocate(song, spec);

        Assert.Equal(8, hardware.Notes.Select(x => x.Voice).Distinct().Count());
        Assert.Equal(0, hardware.DroppedNotes);
        Assert.Equal(0, hardware.ArpeggiatedNotes);
    }

    [Fact]
    public void Genesis_instrument_class_follows_target_voice_not_catalog_id()
    {
        var song = new Song(
        [
            new NoteEvent(0, 480, 60, 100, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0),
            new NoteEvent(0, 480, 64, 90, TrackRole.Arp, SourceTrack: 1, SourceChannel: 1)
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=genesis format=wav");
        var notes = VoiceAllocator.Allocate(song, spec).Notes;

        Assert.Contains(notes, x => x.VoiceClass == "fm");
        Assert.Contains(notes, x => x.VoiceClass == "psg");
        Assert.All(notes, x => Assert.InRange(x.InstrumentId, 0, 79));
    }

    [Fact]
    public void Recognizable_fidelity_protects_existing_lead_from_lower_priority_stealing()
    {
        var song = new Song(
        [
            new NoteEvent(0, 960, 72, 100, TrackRole.Lead),
            new NoteEvent(240, 960, 48, 90, TrackRole.Harmony)
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=pcspeaker fidelity=recognizable format=wav");
        var notes = VoiceAllocator.Allocate(song, spec);

        Assert.Equal(960, notes.Notes.Single(x => x.Role == TrackRole.Lead).DurationTick);
        Assert.Equal(1, notes.DroppedNotes);
    }

    [Fact]
    public void Tracker_articulation_options_reach_hardware_notes()
    {
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=nes note_cut=120 note_delay=15 retrigger=3 pitch_slide=4 volume_slide=-2");
        var note = Assert.Single(VoiceAllocator.Allocate(ChiptuneParser.Compose(spec), spec).Notes);

        Assert.Equal(120, note.NoteCutTicks);
        Assert.Equal(15, note.NoteDelayTicks);
        Assert.Equal(3, note.Retrigger);
        Assert.Equal(4, note.PitchSlide);
        Assert.Equal(-2, note.VolumeSlide);
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
    public void Riff_styles_change_density_rhythm_and_arrangement()
    {
        var styles = new[] { "arcade", "boss", "menu", "racing", "space", "dark", "happy", "chipbreak", "minimal" };
        var signatures = styles.Select(style =>
        {
            var spec = ChiptuneParser.Parse($"generate=riff style={style} key=D scale=minor seed=7 bars=2 format=wav");
            var notes = ChiptuneParser.Compose(spec).Notes;
            return string.Join(';', notes.Select(x => $"{x.StartTick}:{x.DurationTick}:{x.Pitch}:{x.Role}"));
        }).ToArray();

        Assert.True(signatures.Distinct().Count() >= styles.Length - 1, "Most style profiles should produce distinct arrangements.");
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
        Assert.All(hardware.Notes.Where(x=>x.Role==TrackRole.Drums), x=>Assert.Contains(x.Voice, new[] { 3, 4 }));
    }

    [Fact]
    public void Gbc_shares_pulse_channels_across_melodic_parts_without_same_cell_collisions()
    {
        var song = new Song(
        [
            new NoteEvent(0, 960, 76, 110, TrackRole.Lead, SourceTrack: 0, SourceChannel: 0),
            new NoteEvent(0, 960, 64, 90, TrackRole.Harmony, SourceTrack: 1, SourceChannel: 1),
            new NoteEvent(0, 960, 55, 85, TrackRole.Harmony, SourceTrack: 2, SourceChannel: 2),
            new NoteEvent(0, 960, 40, 100, TrackRole.Bass, SourceTrack: 3, SourceChannel: 3)
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=gbc fidelity=preserve format=wav");

        var hardware = VoiceAllocator.Allocate(song, spec);
        var cells = hardware.Notes.GroupBy(x => (x.Voice, x.StartTick));

        Assert.All(cells, cell => Assert.Single(cell));
        Assert.Contains(hardware.Notes, x => x.Voice == 2 && x.Role == TrackRole.Bass);
        Assert.True(hardware.ArpeggiatedNotes > 0);
        Assert.All(hardware.Notes, x => Assert.InRange(x.InstrumentId, 0, 39));
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

        // NES now uses both pulse channels for melodic parts, so the second
        // note no longer steals and truncates the first one.
        Assert.Equal(960, notes[0].DurationTick);
        Assert.Equal(960, notes[1].DurationTick);
        Assert.NotEqual(notes[0].Voice, notes[1].Voice);
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
    [InlineData("atari2600", 1)]
    [InlineData("pcspeaker", 2)]
    [InlineData("zx_spectrum", 0)]
    public void Strict_mode_reports_loss_on_single_or_two_voice_targets(string chip, int minimumDropped)
    {
        var song = new Song(
        [
            new NoteEvent(0, 480, 48, 100, TrackRole.Lead),
            new NoteEvent(0, 480, 55, 100, TrackRole.Harmony),
            new NoteEvent(0, 480, 60, 100, TrackRole.Bass)
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse($"notes=C4/4 chip={chip} fidelity=strict");

        var hardware = VoiceAllocator.Allocate(song, spec);

        Assert.True(hardware.DroppedNotes >= minimumDropped);
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
    public void Midi_import_extends_note_when_pedal_goes_down_after_attack()
    {
        var midi = new byte[]
        {
            (byte)'M',(byte)'T',(byte)'h',(byte)'d', 0,0,0,6, 0,0, 0,1, 1,0xE0,
            (byte)'M',(byte)'T',(byte)'r',(byte)'k', 0,0,0,20,
            0,0x90,60,100, 30,0xB0,64,127, 30,0x80,60,0,
            30,0xB0,64,0, 0,0xFF,0x2F,0
        };
        var spec = ChiptuneParser.Parse($"midi_base64={Convert.ToBase64String(midi)}");
        var note = Assert.Single(ChiptuneParser.Compose(spec).Notes);

        Assert.Equal(180L, note.DurationTick);
    }

    [Fact]
    public void Midi_import_keeps_pitch_bend_automation_on_one_note()
    {
        var midi = new byte[]
        {
            (byte)'M',(byte)'T',(byte)'h',(byte)'d', 0,0,0,6, 0,0, 0,1, 1,0xE0,
            (byte)'M',(byte)'T',(byte)'r',(byte)'k', 0,0,0,20,
            0,0x90,60,100, 30,0xE0,0,96, 30,0xE0,0,64, 30,0x80,60,0,
            0,0xFF,0x2F,0
        };
        var spec = ChiptuneParser.Parse($"midi_base64={Convert.ToBase64String(midi)}");
        var notes = ChiptuneParser.Compose(spec).Notes;

        var note = Assert.Single(notes);
        Assert.Equal(0L, note.StartTick);
        Assert.Equal(180L, note.DurationTick);
        Assert.Equal(60, note.Pitch);
        Assert.Equal([new PitchBendPoint(60, 12288), new PitchBendPoint(120, 8192)], note.PitchBends);
    }

    [Fact]
    public void Midi_controller_changes_do_not_split_or_retrigger_a_note()
    {
        var midi = new byte[]
        {
            (byte)'M',(byte)'T',(byte)'h',(byte)'d', 0,0,0,6, 0,0, 0,1, 1,0xE0,
            (byte)'M',(byte)'T',(byte)'r',(byte)'k', 0,0,0,16,
            0,0x90,60,100, 30,0xB0,7,64, 30,0x80,60,0,
            0,0xFF,0x2F,0
        };
        var spec = ChiptuneParser.Parse($"midi_base64={Convert.ToBase64String(midi)}");
        var note = Assert.Single(ChiptuneParser.Compose(spec).Notes);

        Assert.Equal(120L, note.DurationTick);
        var controller = Assert.Single(note.ControllerChanges!);
        Assert.Equal(60L, controller.Tick);
        Assert.Equal(64, controller.Volume);
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

        Assert.Contains(hardware, x => x.Instrument == "bass" && x.InstrumentId == 3);
        Assert.Contains(hardware, x => x.Instrument == "kick" && x.InstrumentId == 16);
        Assert.Contains(hardware, x => x.Instrument == "hat" && x.InstrumentId != x.Voice && x.VoiceClass == "noise");
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

    [Fact]
    public void Managed_renderer_produces_non_silent_audio_without_full_scale_clipping()
    {
        var spec = ChiptuneParser.Parse("notes=\"C4/8 E4/8 G4/4\" chip=gbc format=wav");
        var bytes = ManagedChipRenderer.Render(VoiceAllocator.Allocate(ChiptuneParser.Compose(spec), spec));
        var samples = Enumerable.Range(0, (bytes.Length - 44) / 2)
            .Select(i => BitConverter.ToInt16(bytes, 44 + i * 2))
            .ToArray();
        var peak = samples.Select(Math.Abs).Max();
        var rms = Math.Sqrt(samples.Select(x => (double)x * x).Average());

        Assert.True(rms > 100, "Rendered signal is effectively silent.");
        Assert.True(peak < short.MaxValue, "Renderer is producing hard full-scale clipping.");
    }

    [Fact]
    public void Nes_drum_allocator_prefers_DPCM_for_kick_and_noise_for_hat()
    {
        var song = new Song(
        [
            new NoteEvent(0, 120, 36, 110, TrackRole.Drums),
            new NoteEvent(120, 120, 42, 90, TrackRole.Drums)
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=C4/4 chip=nes format=wav");
        var notes = VoiceAllocator.Allocate(song, spec).Notes.OrderBy(x => x.StartTick).ToArray();

        Assert.Equal(4, notes[0].Voice);
        Assert.Equal("kick", notes[0].Instrument);
        Assert.Equal(3, notes[1].Voice);
        Assert.Equal("hat", notes[1].Instrument);
    }

    [Theory]
    [InlineData("gb")]
    [InlineData("gbc")]
    [InlineData("nes")]
    [InlineData("snes")]
    [InlineData("sms")]
    [InlineData("c64_6581")]
    [InlineData("c64_8580")]
    [InlineData("genesis")]
    [InlineData("pce")]
    [InlineData("atari2600")]
    [InlineData("pokey")]
    [InlineData("pcspeaker")]
    [InlineData("zx_spectrum")]
    public void Every_chip_profile_produces_non_silent_managed_audio(string chip)
    {
        var spec = ChiptuneParser.Parse($"generate=song chip={chip} bars=1 format=wav");
        var wav = ManagedChipRenderer.Render(VoiceAllocator.Allocate(ChiptuneParser.Compose(spec), spec));
        var samples = Enumerable.Range(0, (wav.Length - 44) / 2).Select(i => BitConverter.ToInt16(wav, 44 + i * 2));

        Assert.True(samples.Any(x => x != 0), $"{chip} produced silence.");
    }
}
