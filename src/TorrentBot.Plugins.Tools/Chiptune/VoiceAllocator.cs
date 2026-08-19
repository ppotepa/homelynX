namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class VoiceAllocator
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<TrackRole, int[]>> Maps =
        new Dictionary<string, IReadOnlyDictionary<TrackRole, int[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["gb"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3] },
            ["gbc"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3] },
            ["gameboy"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3] },
            ["nes"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3] },
            ["sms"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3] },
            ["snes"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0,1], [TrackRole.Bass]=[2], [TrackRole.Drums]=[3,4,5], [TrackRole.Harmony]=[6], [TrackRole.Arp]=[7] }
            , ["c64_6581"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[2], [TrackRole.Bass]=[1], [TrackRole.Drums]=[2] }
            , ["c64_8580"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1], [TrackRole.Arp]=[2], [TrackRole.Bass]=[1], [TrackRole.Drums]=[2] }
            , ["genesis"] = new Dictionary<TrackRole,int[]> { [TrackRole.Lead]=[0], [TrackRole.Harmony]=[1,2], [TrackRole.Arp]=[3], [TrackRole.Bass]=[4], [TrackRole.Drums]=[5] }
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
                var voice = voices.FirstOrDefault(x => voiceUntil.GetValueOrDefault(x) <= note.StartTick, -1);
                if (voice < 0 && spec.Fidelity != "strict" && (note.Role is TrackRole.Lead or TrackRole.Harmony) && voices.Length == 1 && group.Count() > 1)
                {
                    // A monophonic target expresses a simultaneous chord as a
                    // short deterministic arpeggio instead of silently losing it.
                    var slice = Math.Max(60, note.DurationTick / Math.Max(1, group.Count()));
                    var arpStart = note.StartTick + allocated.Count(x => x.StartTick >= note.StartTick && x.Voice == voices[0]) * slice;
                    var arp = ToHardware(note, voices[0], spec, InstrumentIdFor(note.Role));
                    allocated.Add(arp with { StartTick = arpStart, DurationTick = Math.Min(slice, note.DurationTick) });
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
                var hardware = ToHardware(note, voice, spec, InstrumentIdFor(note.Role));
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
        var bendSemitones = (note.PitchBend - 8192) / 8192d * note.PitchBendRange;
        var pitch = Math.Clamp(note.Pitch + (int)Math.Round(bendSemitones, MidpointRounding.AwayFromZero), 0, 127);
        return new(voice, note.StartTick, note.DurationTick, pitch, note.Velocity,
            InstrumentFor(note, spec.Instrument), note.Role, instrumentId,
            note.Pan, note.Expression, note.PitchBend, note.Program);
    }

    private static int InstrumentIdFor(TrackRole role) => role switch
    {
        TrackRole.Lead => 0, TrackRole.Harmony => 1, TrackRole.Bass => 2, TrackRole.Drums => 3, _ => 0
    };

    private static string InstrumentFor(NoteEvent note, string requested)
    {
        if (note.Role == TrackRole.Drums) return "drums";
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
}
