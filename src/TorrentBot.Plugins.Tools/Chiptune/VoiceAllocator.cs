namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class VoiceAllocator
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<TrackRole, int[]>> Maps =
        new Dictionary<string, IReadOnlyDictionary<TrackRole, int[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["gb"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3] },
            ["gbc"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3] },
            ["gameboy"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3] },
            ["nes"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3,4] },
            ["sms"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3] },
            ["snes"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0,1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3,4,5], [TrackRole.Harmony]=[6], [TrackRole.Arp]=[7] }
            , ["c64_6581"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[2], [TrackRole.Bass]=[1], [TrackRole.Drums]=[2] }
            , ["c64_8580"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[2], [TrackRole.Bass]=[1], [TrackRole.Drums]=[2] }
            // Genesis/Mega Drive: FM1-6 (0..5) plus PSG tone/noise (6..9).
            // Keep bass on FM and reserve the PSG for cheap harmony/arp and
            // percussion, while still allowing polyphonic lead material.
            , ["genesis"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0,1], [TrackRole.Harmony]=[2,3,6,7], [TrackRole.Arp]=[6,7,8], [TrackRole.Bass]=[4], [TrackRole.Drums]=[9] }
            , ["pce"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[2,3], [TrackRole.Bass]=[4], [TrackRole.Drums]=[5] }
            , ["atari2600"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Bass]=[1], [TrackRole.Drums]=[1] }
            , ["pokey"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[2], [TrackRole.Bass]=[3], [TrackRole.Drums]=[3] }
            , ["pcspeaker"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Bass]=[0], [TrackRole.Drums]=[0] }
            , ["zx_spectrum"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Bass]=[0], [TrackRole.Drums]=[0] }
        };

    private static readonly IReadOnlyDictionary<TrackRole,int> Priority = new Dictionary<TrackRole,int>
    { [TrackRole.Lead]=5, [TrackRole.Bass]=4, [TrackRole.Drums]=3, [TrackRole.Arp]=2, [TrackRole.Harmony]=1 };

    public static HardwareSong Allocate(Song song, ChiptuneSpec spec)
    {
        var map = Maps[spec.Chip];
        var allocated = new List<HardwareNote>();
        var voiceUntil = new Dictionary<int, long>();
        var lastOnVoice = new Dictionary<int, int>();
        var revoiced = 0; var arpeggiated = 0; var dropped = 0;
        foreach (var group in song.Notes.GroupBy(x => x.StartTick).OrderBy(x => x.Key))
        {
            foreach (var note in group.OrderByDescending(x => Priority[x.Role]).ThenByDescending(x => x.Velocity).ThenByDescending(x => x.Pitch))
            {
                var voices = map.TryGetValue(note.Role, out var mapped) ? mapped : map[TrackRole.Lead];
                if (spec.Chip == "nes" && note.Role == TrackRole.Drums && IsDpcmPercussion(note.Pitch))
                    voices = [4, 3];
                var voice = voices.FirstOrDefault(x => voiceUntil.GetValueOrDefault(x) <= note.StartTick, -1);
                if (voice < 0 && spec.Fidelity == "preserve" && (note.Role is TrackRole.Lead or TrackRole.Harmony) && voices.Length == 1 && group.Count() > 1)
                {
                    // A monophonic target expresses a simultaneous chord as a
                    // short deterministic arpeggio instead of silently losing it.
                    var slice = Math.Max(60, note.DurationTick / Math.Max(1, group.Count()));
                    var arpStart = note.StartTick + allocated.Count(x => x.StartTick >= note.StartTick && x.Voice == voices[0]) * slice;
                    var arp = ToHardware(note, voices[0], spec, InstrumentIdFor(note));
                    allocated.Add(arp with { StartTick = arpStart, DurationTick = Math.Min(slice, note.DurationTick) });
                    voiceUntil[voices[0]] = Math.Max(voiceUntil.GetValueOrDefault(voices[0]), arpStart + Math.Min(slice, note.DurationTick));
                    lastOnVoice[voices[0]] = allocated.Count - 1;
                    arpeggiated++;
                    continue;
                }
                if (voice < 0)
                {
                    if (spec.Fidelity == "strict") { dropped++; continue; }
                    // Stateful voice stealing: shorten only the note that is
                    // actually being replaced, never a later note by accident.
                    voice = voices.OrderBy(x => voiceUntil.GetValueOrDefault(x)).First();
                    if (lastOnVoice.TryGetValue(voice, out var previousIndex))
                    {
                        var previous = allocated[previousIndex];
                        if (previous.StartTick < note.StartTick)
                            allocated[previousIndex] = previous with { DurationTick = Math.Max(1, note.StartTick - previous.StartTick) };
                    }
                    revoiced++;
                }
                var hardware = ToHardware(note, voice, spec, InstrumentIdFor(note));
                lastOnVoice[voice] = allocated.Count;
                allocated.Add(hardware);
                voiceUntil[voice] = note.EndTick;
            }
        }
        return new HardwareSong(spec.Chip, spec.Bpm, spec.SampleRate, song.TempoMap.Points, allocated.OrderBy(x => x.StartTick).ThenBy(x => x.Voice).ToArray(), song.EndTick,
            spec.Wave, spec.Duty, spec.Attack, spec.Decay, spec.Sustain, spec.Release, spec.Vibrato, spec.Filter,
            song.Notes.Count, revoiced, arpeggiated, dropped, spec.Fidelity);
    }

    private static HardwareNote ToHardware(NoteEvent note, int voice, ChiptuneSpec spec, int instrumentId)
    {
        // MIDI importer expands bend automation into short note segments. Do
        // not apply the same bend twice here; the value remains on the event
        // for diagnostics and future native tracker automation.
        return new(voice, note.StartTick, note.DurationTick, Math.Clamp(note.Pitch, 0, 127), note.Velocity,
            InstrumentFor(note, spec.Instrument), note.Role, instrumentId,
            note.Pan, note.Expression, note.PitchBend, note.Program);
    }

    private static int InstrumentIdFor(NoteEvent note)
    {
        // Keep IDs deterministic and independent of hardware voice. Program
        // changes therefore select a different patch while a part moves
        // between voices. Drum IDs are semantic GM percussion families.
        return note.Role switch
        {
            TrackRole.Drums => 200 + PercussionFamily(note.Pitch),
            _ => 10 + Math.Clamp(note.Program, 0, 127)
        };
    }

    private static int PercussionFamily(int pitch) => pitch switch
    {
        35 or 36 => 0, // kick
        38 or 40 or 37 or 39 => 1, // snare/clap
        42 or 44 => 2, // closed hat/pedal hat
        46 => 3, // open hat
        49 or 55 or 57 => 5, // crash/splash
        51 or 53 or 59 => 6, // ride/bell
        >= 41 and <= 50 => 4, // toms
        _ => 7
    };

    private static bool IsDpcmPercussion(int pitch) => pitch is 35 or 36 or 37 or 38 or 39 or 40;

    private static string InstrumentFor(NoteEvent note, string requested)
    {
        if (note.Role == TrackRole.Drums) return PercussionName(note.Pitch);
        if (note.Role == TrackRole.Bass) return "bass";
        return note.Program switch
        {
            >= 32 and <= 39 => "bass",
            >= 40 and <= 55 => "strings",
            >= 56 and <= 63 => "brass",
            >= 64 and <= 79 => "reed",
            >= 80 and <= 87 => "lead",
            >= 88 and <= 103 => "pad",
            >= 104 and <= 111 => "lead",
            >= 112 and <= 119 => "pad",
            _ => requested
        };
    }

    private static string PercussionName(int pitch) => pitch switch
    {
        35 or 36 => "kick",
        37 or 38 or 39 or 40 => "snare",
        42 or 44 => "hat",
        46 => "open_hat",
        49 or 55 or 57 => "crash",
        51 or 53 or 59 => "ride",
        >= 41 and <= 50 => "tom",
        _ => "drums"
    };
}
