namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class VoiceAllocator
{
    private readonly record struct PartKey(int Track, int Channel, int Program, int Bank, TrackRole Role);
    private readonly record struct VoiceKey(PartKey Part, int Lane);
    private sealed record PartInfo(int Priority);
    private sealed record LaneInfo(int Lane, int PriorityBonus);

    private sealed class LaneState
    {
        public long EndTick { get; set; }
        public int LastPitch { get; set; }
        public List<NoteEvent> Notes { get; } = [];
    }

    public static HardwareSong Allocate(Song song, ChiptuneSpec spec)
    {
        var profile = ChipProfile.For(spec.Chip);
        var partInfo = AnalyzeParts(song);
        var laneInfo = AnalyzeLanes(song);
        var allocated = new List<HardwareNote>();
        var allocatedPriority = new List<int>();
        var allocatedVoiceKeys = new List<VoiceKey>();
        var voiceUntil = new Dictionary<int, long>();
        var lastOnVoice = new Dictionary<int, int>();
        var preferredVoice = new Dictionary<VoiceKey, int>();
        var arpCursor = new Dictionary<(long GroupStart, int Voice), long>();
        var revoiced = 0; var arpeggiated = 0; var dropped = 0;

        foreach (var group in song.Notes.GroupBy(x => x.StartTick).OrderBy(x => x.Key))
        {
            foreach (var note in group
                         .OrderByDescending(x => PriorityOf(x, partInfo, laneInfo))
                         .ThenByDescending(x => x.Velocity)
                         .ThenByDescending(x => x.Pitch))
            {
                var voices = profile.Candidates(note);
                if (voices.Count == 0) { dropped++; continue; }

                var part = KeyOf(note);
                var lane = laneInfo[note];
                var voiceKey = new VoiceKey(part, lane.Lane);
                var notePriority = PriorityOf(note, partInfo, laneInfo);
                var preferred = preferredVoice.GetValueOrDefault(voiceKey, -1);
                var voice = preferred >= 0 && voices.Contains(preferred) && voiceUntil.GetValueOrDefault(preferred) <= note.StartTick
                    ? preferred
                    : BestFreeVoice(voices, voiceUntil, preferredVoice, voiceKey, note.StartTick);

                if (voice < 0 && spec.Fidelity != "strict" && note.Role != TrackRole.Drums && group.Count() > 1)
                {
                    var arpVoice = voices.FirstOrDefault(candidate =>
                        arpCursor.ContainsKey((note.StartTick, candidate)) ||
                        (lastOnVoice.TryGetValue(candidate, out var index) && allocated[index].StartTick == note.StartTick), -1);
                    if (arpVoice >= 0)
                    {
                        var slice = Math.Max(1, note.DurationTick / Math.Max(1, group.Count()));
                        var cursorKey = (note.StartTick, arpVoice);
                        if (!arpCursor.TryGetValue(cursorKey, out var arpStart))
                        {
                            var previousIndex = lastOnVoice[arpVoice];
                            var previous = allocated[previousIndex];
                            var previousDuration = Math.Min(slice, previous.DurationTick);
                            allocated[previousIndex] = TrimToSpan(previous, previous.StartTick, previousDuration);
                            arpStart = previous.StartTick + previousDuration;
                        }
                        var available = note.EndTick - arpStart;
                        if (available <= 0)
                        {
                            dropped++;
                            continue;
                        }
                        var duration = Math.Min(slice, available);
                        var arp = TrimToSpan(ToHardware(note, arpVoice, spec), arpStart, duration);
                        allocated.Add(arp);
                        allocatedPriority.Add(notePriority);
                        allocatedVoiceKeys.Add(voiceKey);
                        arpCursor[cursorKey] = arpStart + duration;
                        voiceUntil[arpVoice] = arpStart + duration;
                        lastOnVoice[arpVoice] = allocated.Count - 1;
                        preferredVoice[voiceKey] = arpVoice;
                        if (spec.Fidelity == "preserve") arpeggiated++;
                        else revoiced++;
                        continue;
                    }
                }

                if (voice < 0)
                {
                    if (spec.Fidelity == "strict") { dropped++; continue; }

                    var continuationVoice = preferred >= 0 && voices.Contains(preferred) &&
                                            lastOnVoice.TryGetValue(preferred, out var continuationIndex) &&
                                            allocatedVoiceKeys[continuationIndex] == voiceKey
                        ? preferred
                        : -1;
                    if (continuationVoice >= 0)
                    {
                        voice = continuationVoice;
                        var previousIndex = lastOnVoice[voice];
                        var previous = allocated[previousIndex];
                        if (previous.StartTick < note.StartTick)
                            allocated[previousIndex] = TrimToSpan(previous, previous.StartTick, Math.Max(1, note.StartTick - previous.StartTick));
                        revoiced++;
                    }
                    else
                    {
                        voice = voices
                            .OrderBy(candidate => lastOnVoice.TryGetValue(candidate, out var index) ? allocatedPriority[index] : int.MinValue)
                            .ThenBy(candidate => voiceUntil.GetValueOrDefault(candidate))
                            .First();

                        if (lastOnVoice.TryGetValue(voice, out var previousIndex))
                        {
                            var previous = allocated[previousIndex];
                            var previousPriority = allocatedPriority[previousIndex];
                            if (previous.StartTick >= note.StartTick)
                            {
                                dropped++;
                                continue;
                            }
                            if (spec.Fidelity == "recognizable" && previousPriority >= notePriority)
                            {
                                dropped++;
                                continue;
                            }
                            if (previous.StartTick < note.StartTick)
                                allocated[previousIndex] = TrimToSpan(previous, previous.StartTick, Math.Max(1, note.StartTick - previous.StartTick));
                        }
                        revoiced++;
                    }
                }

                var hardware = ToHardware(note, voice, spec);
                lastOnVoice[voice] = allocated.Count;
                allocated.Add(hardware);
                allocatedPriority.Add(notePriority);
                allocatedVoiceKeys.Add(voiceKey);
                preferredVoice[voiceKey] = voice;
                voiceUntil[voice] = note.EndTick;
            }
        }

        EnsureMonophonicTimelines(allocated);
        var ordered = allocated.OrderBy(x => x.StartTick).ThenBy(x => x.Voice).ToArray();
        var endTick = ordered.Length == 0 ? song.EndTick : Math.Max(song.EndTick, ordered.Max(x => x.StartTick + x.DurationTick));
        return new HardwareSong(spec.Chip, spec.Bpm, spec.SampleRate, song.TempoMap.Points, ordered, endTick,
            spec.Wave, spec.Duty, spec.Attack, spec.Decay, spec.Sustain, spec.Release, spec.Vibrato, spec.Filter,
            song.Notes.Count, revoiced, arpeggiated, dropped, spec.Fidelity);
    }

    private static int BestFreeVoice(
        IReadOnlyList<int> voices,
        IReadOnlyDictionary<int, long> voiceUntil,
        IReadOnlyDictionary<VoiceKey, int> preferredVoice,
        VoiceKey voiceKey,
        long startTick)
    {
        var previous = preferredVoice.GetValueOrDefault(voiceKey, -1);
        if (previous >= 0 && voices.Contains(previous) && voiceUntil.GetValueOrDefault(previous) <= startTick)
            return previous;
        return voices.FirstOrDefault(x => voiceUntil.GetValueOrDefault(x) <= startTick, -1);
    }

    private static Dictionary<PartKey, PartInfo> AnalyzeParts(Song song)
    {
        var trackNames = song.MidiMetadata?.TrackNames ?? new Dictionary<int, string>();
        return song.Notes
            .GroupBy(KeyOf)
            .ToDictionary(group => group.Key, group =>
            {
                var notes = group.ToArray();
                var peak = PeakOverlap(notes);
                var averageVelocity = notes.Average(x => x.Velocity);
                var trackName = group.Key.Track >= 0 ? trackNames.GetValueOrDefault(group.Key.Track, string.Empty) : string.Empty;
                var priority = BaseRolePriority(group.Key.Role) + ProgramPriority(group.Key.Program, group.Key.Role);
                if (peak <= 1) priority += 15;
                else if (peak >= 5) priority -= 8;
                priority += (int)Math.Round((averageVelocity - 64) / 8d);
                priority += NamePriority(trackName, group.Key.Role);
                return new PartInfo(Math.Clamp(priority, 1, 200));
            });
    }

    private static Dictionary<NoteEvent, LaneInfo> AnalyzeLanes(Song song)
    {
        var result = new Dictionary<NoteEvent, LaneInfo>(ReferenceEqualityComparer.Instance);
        foreach (var group in song.Notes.GroupBy(KeyOf))
        {
            if (group.Key.Role == TrackRole.Drums)
            {
                foreach (var note in group)
                    result[note] = new LaneInfo(1000 + PercussionFamily(note.Pitch), 0);
                continue;
            }

            var lanes = new List<LaneState>();
            foreach (var note in group.OrderBy(x => x.StartTick).ThenBy(x => x.Pitch))
            {
                var candidate = lanes
                    .Select((lane, index) => (lane, index))
                    .Where(x => x.lane.EndTick <= note.StartTick)
                    .OrderBy(x => Math.Abs(x.lane.LastPitch - note.Pitch))
                    .ThenByDescending(x => x.lane.EndTick)
                    .FirstOrDefault();

                int laneIndex;
                if (candidate.lane is null)
                {
                    laneIndex = lanes.Count;
                    lanes.Add(new LaneState());
                }
                else laneIndex = candidate.index;

                var lane = lanes[laneIndex];
                lane.EndTick = note.KeyEndTick;
                lane.LastPitch = note.Pitch;
                lane.Notes.Add(note);
                result[note] = new LaneInfo(laneIndex, 0);
            }

            if (lanes.Count == 0) continue;
            var characteristics = lanes
                .Select((lane, index) => new
                {
                    Index = index,
                    Median = lane.Notes.Select(x => x.Pitch).Order().ElementAt(lane.Notes.Count / 2),
                    Velocity = lane.Notes.Average(x => x.Velocity),
                    Count = lane.Notes.Count
                })
                .OrderBy(x => x.Median)
                .ToArray();
            var lowest = characteristics[0].Index;
            var highest = characteristics[^1].Index;

            foreach (var characteristic in characteristics)
            {
                var bonus = 0;
                if (lanes.Count == 1) bonus = 8;
                else
                {
                    if (characteristic.Index == highest) bonus += group.Key.Program is >= 88 and <= 103 ? 5 : 16;
                    if (characteristic.Index == lowest && group.Key.Program is >= 0 and <= 39) bonus += 10;
                    if (characteristic.Velocity >= 96) bonus += 3;
                    if (characteristic.Count <= 2) bonus -= 2;
                }
                foreach (var note in lanes[characteristic.Index].Notes)
                    result[note] = new LaneInfo(characteristic.Index, bonus);
            }
        }
        return result;
    }

    private static int PriorityOf(NoteEvent note, IReadOnlyDictionary<PartKey, PartInfo> parts, IReadOnlyDictionary<NoteEvent, LaneInfo> lanes)
        => Math.Clamp(parts[KeyOf(note)].Priority + lanes[note].PriorityBonus, 1, 240);

    private static PartKey KeyOf(NoteEvent note)
        => new(note.SourceTrack, note.SourceChannel, note.Program, note.Bank, note.Role);

    private static int BaseRolePriority(TrackRole role) => role switch
    {
        TrackRole.Lead => 100,
        TrackRole.Bass => 95,
        TrackRole.CounterLead => 90,
        TrackRole.Drums => 85,
        TrackRole.Arp => 60,
        _ => 45
    };

    private static int ProgramPriority(int program, TrackRole role)
    {
        if (role == TrackRole.Drums) return 20;
        return program switch
        {
            >= 80 and <= 87 => 32,
            >= 72 and <= 79 => 26,
            >= 64 and <= 71 => 25,
            >= 56 and <= 63 => 24,
            >= 32 and <= 39 => 28,
            >= 24 and <= 31 => 22,
            >= 0 and <= 7 => 20,
            >= 8 and <= 15 => 12,
            >= 16 and <= 23 => 12,
            >= 40 and <= 55 => 8,
            >= 88 and <= 103 => -12,
            >= 104 and <= 111 => 12,
            >= 112 and <= 119 => 4,
            _ => 0
        };
    }

    private static int NamePriority(string name, TrackRole role)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        name = name.ToLowerInvariant();
        var score = 0;
        if (name.Contains("lead") || name.Contains("melody") || name.Contains("solo") || name.Contains("theme")) score += 30;
        if (name.Contains("counter")) score += role == TrackRole.CounterLead ? 35 : 12;
        if (name.Contains("bass")) score += 24;
        if (name.Contains("drum") || name.Contains("perc")) score += role == TrackRole.Drums ? 20 : 5;
        if (name.Contains("pad") || name.Contains("chord") || name.Contains("ambience")) score -= 10;
        return score;
    }

    private static int PeakOverlap(IEnumerable<NoteEvent> notes)
    {
        var active = 0;
        var peak = 0;
        foreach (var point in notes
                     .SelectMany(x => new[] { (Tick: x.StartTick, Delta: 1), (Tick: x.EndTick, Delta: -1) })
                     .OrderBy(x => x.Tick)
                     .ThenBy(x => x.Delta))
        {
            active += point.Delta;
            peak = Math.Max(peak, active);
        }
        return peak;
    }

    private static HardwareNote TrimToSpan(HardwareNote note, long startTick, long durationTick)
    {
        durationTick = Math.Max(1, durationTick);
        var endTick = startTick + durationTick;
        var bends = note.PitchBends?.Where(x => x.Tick > startTick && x.Tick < endTick).ToArray();
        var controllers = note.ControllerChanges?.Where(x => x.Tick > startTick && x.Tick < endTick).ToArray();
        var noteCut = note.NoteCutTicks < 0
            ? -1
            : Math.Min(note.NoteCutTicks, Math.Max(0, (int)Math.Min(int.MaxValue, durationTick - 1)));
        return note with
        {
            StartTick = startTick,
            DurationTick = durationTick,
            PitchBends = bends,
            ControllerChanges = controllers,
            NoteCutTicks = noteCut
        };
    }

    private static void EnsureMonophonicTimelines(IReadOnlyList<HardwareNote> notes)
    {
        foreach (var voice in notes.GroupBy(x => x.Voice))
        {
            HardwareNote? previous = null;
            foreach (var current in voice.OrderBy(x => x.StartTick).ThenBy(x => x.DurationTick))
            {
                if (previous is not null && current.StartTick < previous.StartTick + previous.DurationTick)
                    throw new InvalidOperationException($"Chiptune arranger produced overlapping notes on hardware voice {voice.Key}.");
                previous = current;
            }
        }
    }

    private static HardwareNote ToHardware(NoteEvent note, int voice, ChiptuneSpec spec)
    {
        var profile = ChipProfile.For(spec.Chip);
        var voiceClass = profile.Voice(voice).Class;
        var patch = !IsAuto(note.Patch) ? NormalizePatch(note.Patch) : InstrumentFor(note, spec.Instrument);
        return new(voice, note.StartTick, note.DurationTick, Math.Clamp(note.Pitch, 0, 127), note.Velocity,
            patch, note.Role, InstrumentCatalog.Id(patch, voiceClass),
            note.Pan, note.Expression, note.PitchBend, note.PitchBendRange, note.Program,
            note.NoteCutTicks, note.NoteDelayTicks, note.Retrigger, note.PitchSlide, note.VolumeSlide,
            note.Volume, note.Modulation, note.Aftertouch, note.ReleaseVelocity, note.PitchBends, note.ControllerChanges,
            voiceClass.ToString().ToLowerInvariant(), note.Section, note.SectionIntensity);
    }

    private static int PercussionFamily(int pitch) => pitch switch
    {
        35 or 36 => 0,
        37 or 38 or 39 or 40 => 1,
        42 or 44 => 2,
        46 => 3,
        49 or 55 or 57 => 5,
        51 or 53 or 59 => 6,
        >= 41 and <= 50 => 4,
        _ => 7
    };

    private static string InstrumentFor(NoteEvent note, string requested)
    {
        if (note.Role == TrackRole.Drums) return PercussionName(note.Pitch);
        if (note.Role == TrackRole.Bass || note.Program is >= 32 and <= 39) return "bass";
        if (!IsAuto(requested)) return NormalizePatch(requested);
        if (note.SourceTrack < 0)
            return note.Role switch
            {
                TrackRole.CounterLead => "pluck",
                TrackRole.Arp => "pluck",
                TrackRole.Harmony => "soft_lead",
                _ => "lead"
            };
        return note.Program switch
        {
            >= 0 and <= 7 => "epiano",
            >= 8 and <= 15 => "bell",
            >= 16 and <= 23 => "organ",
            >= 24 and <= 31 => "pluck",
            >= 40 and <= 55 => "strings",
            >= 56 and <= 63 => "brass",
            >= 64 and <= 71 => "reed",
            >= 72 and <= 79 => "flute",
            >= 80 and <= 87 => "lead",
            >= 88 and <= 103 => "pad",
            >= 104 and <= 107 => "pluck",
            108 => "bell",
            109 => "reed",
            110 => "strings",
            111 => "reed",
            >= 112 and <= 114 => "bell",
            >= 115 and <= 119 => "pluck",
            >= 120 and <= 127 => "pad",
            _ => note.Role == TrackRole.CounterLead ? "pluck" : "lead"
        };
    }

    private static string NormalizePatch(string patch) => patch.Equals("arp", StringComparison.OrdinalIgnoreCase) ? "pluck" : patch.ToLowerInvariant();
    private static bool IsAuto(string? patch) => string.IsNullOrWhiteSpace(patch) || patch.Equals("auto", StringComparison.OrdinalIgnoreCase);

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
