namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class VoiceAllocator
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<TrackRole, int[]>> Maps =
        new Dictionary<string, IReadOnlyDictionary<TrackRole, int[]>>(StringComparer.OrdinalIgnoreCase)
        {
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
        foreach (var group in song.Notes.GroupBy(x => x.StartTick).OrderBy(x => x.Key))
        {
            var occupied = new HashSet<int>();
            foreach (var note in group.OrderByDescending(x => Priority[x.Role]).ThenByDescending(x => x.Velocity).ThenByDescending(x => x.Pitch))
            {
                var voices = map.TryGetValue(note.Role, out var mapped) ? mapped : map[TrackRole.Lead];
                var voice = voices.FirstOrDefault(x => !occupied.Contains(x), -1);
                if (voice < 0 && note.Role is TrackRole.Lead or TrackRole.Harmony && voices.Length == 1)
                {
                    // Monophonic chips express simultaneous harmony as a fast deterministic arpeggio.
                    var slice = Math.Max(60, note.DurationTick / Math.Max(1, group.Count()));
                    allocated.Add(new HardwareNote(voices[0], note.StartTick + allocated.Count(x => x.StartTick == note.StartTick && x.Voice == voices[0]) * slice, Math.Min(slice, note.DurationTick), note.Pitch, note.Velocity, spec.Instrument, note.Role));
                    continue;
                }
                if (voice < 0) continue;
                occupied.Add(voice);
                allocated.Add(new HardwareNote(voice, note.StartTick, note.DurationTick, note.Pitch, note.Velocity, InstrumentFor(note.Role, spec.Instrument), note.Role));
            }
        }
        var normalized = NormalizeMonophonicVoices(allocated);
        return new HardwareSong(spec.Chip, spec.Bpm, spec.SampleRate, song.TempoMap.Points, normalized, song.EndTick,
            spec.Wave, spec.Duty, spec.Attack, spec.Decay, spec.Sustain, spec.Release, spec.Vibrato, spec.Filter);
    }

    private static IReadOnlyList<HardwareNote> NormalizeMonophonicVoices(IEnumerable<HardwareNote> source)
    {
        var result = new List<HardwareNote>();
        foreach (var voice in source.GroupBy(x => x.Voice))
        {
            var notes = voice.OrderBy(x => x.StartTick).ThenByDescending(x => x.Velocity).ToArray();
            for (var i = 0; i < notes.Length; i++)
            {
                var note = notes[i];
                if (i + 1 < notes.Length && notes[i + 1].StartTick > note.StartTick)
                {
                    note = note with { DurationTick = Math.Min(note.DurationTick, notes[i + 1].StartTick - note.StartTick) };
                }
                result.Add(note);
            }
        }
        return result.OrderBy(x => x.StartTick).ThenBy(x => x.Voice).ToArray();
    }

    private static string InstrumentFor(TrackRole role, string requested) => role switch
    { TrackRole.Bass => "bass", TrackRole.Drums => "drums", TrackRole.Arp => "arp", _ => requested };
}
