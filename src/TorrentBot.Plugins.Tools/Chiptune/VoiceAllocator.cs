namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class VoiceAllocator
{
    private static readonly IReadOnlyDictionary<TrackRole,int> Priority = new Dictionary<TrackRole,int>
    { [TrackRole.Lead]=5, [TrackRole.Bass]=4, [TrackRole.Drums]=3, [TrackRole.Arp]=2, [TrackRole.Harmony]=1 };

    public static HardwareSong Allocate(Song song, ChiptuneSpec spec)
    {
        var profile = ChipProfile.For(spec.Chip);
        var allocated = new List<HardwareNote>();
        var voiceUntil = new Dictionary<int, long>();
        var lastOnVoice = new Dictionary<int, int>();
        var preferredVoice = new Dictionary<(int Track, int Channel, int Program, int Bank, TrackRole Role), int>();
        var arpCursor = new Dictionary<(long GroupStart, int Voice), long>();
        var revoiced = 0; var arpeggiated = 0; var dropped = 0;
        foreach (var group in song.Notes.GroupBy(x => x.StartTick).OrderBy(x => x.Key))
        {
            foreach (var note in group.OrderByDescending(x => Priority[x.Role]).ThenByDescending(x => x.Velocity).ThenByDescending(x => x.Pitch))
            {
                var voices = profile.Candidates(note);
                var part = (note.SourceTrack, note.SourceChannel, note.Program, note.Bank, note.Role);
                var preferred = preferredVoice.GetValueOrDefault(part, -1);
                var voice = preferred >= 0 && voices.Contains(preferred) && voiceUntil.GetValueOrDefault(preferred) <= note.StartTick
                    ? preferred
                    : voices.FirstOrDefault(x => voiceUntil.GetValueOrDefault(x) <= note.StartTick, -1);

                if (voice < 0 && spec.Fidelity != "strict" && note.Role != TrackRole.Drums && group.Count() > 1)
                {
                    // If this hardware voice already contains another note from
                    // the same simultaneous chord, turn the chord into a real
                    // non-overlapping arpeggio. The first chord note is shortened
                    // to its slice; later notes continue from the shared cursor.
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
                        arpCursor[cursorKey] = arpStart + duration;
                        voiceUntil[arpVoice] = arpStart + duration;
                        lastOnVoice[arpVoice] = allocated.Count - 1;
                        preferredVoice[part] = arpVoice;
                        if (spec.Fidelity == "preserve") arpeggiated++;
                        else revoiced++;
                        continue;
                    }
                }

                if (voice < 0)
                {
                    if (spec.Fidelity == "strict") { dropped++; continue; }
                    // Stateful voice stealing: shorten only the note that is
                    // actually being replaced. Any automation after the new end
                    // belongs to the old source note and must not leak onto the
                    // replacement now playing on this hardware voice.
                    voice = voices.OrderBy(x => voiceUntil.GetValueOrDefault(x)).First();
                    if (lastOnVoice.TryGetValue(voice, out var previousIndex))
                    {
                        var previous = allocated[previousIndex];
                        if (spec.Fidelity == "recognizable" && Priority[previous.Role] >= Priority[note.Role])
                        {
                            dropped++;
                            continue;
                        }
                        if (previous.StartTick < note.StartTick)
                        {
                            var duration = Math.Max(1, note.StartTick - previous.StartTick);
                            allocated[previousIndex] = TrimToSpan(previous, previous.StartTick, duration);
                        }
                    }
                    revoiced++;
                }
                var hardware = ToHardware(note, voice, spec);
                lastOnVoice[voice] = allocated.Count;
                preferredVoice[part] = voice;
                allocated.Add(hardware);
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

    private static HardwareNote TrimToSpan(HardwareNote note, long startTick, long durationTick)
    {
        durationTick = Math.Max(1, durationTick);
        var endTick = startTick + durationTick;
        var bends = note.PitchBends?
            .Where(x => x.Tick > startTick && x.Tick < endTick)
            .ToArray();
        var controllers = note.ControllerChanges?
            .Where(x => x.Tick > startTick && x.Tick < endTick)
            .ToArray();
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
        var patch = InstrumentFor(note, spec.Instrument);
        return new(voice, note.StartTick, note.DurationTick, Math.Clamp(note.Pitch, 0, 127), note.Velocity,
            patch, note.Role, InstrumentCatalog.Id(patch, voiceClass),
            note.Pan, note.Expression, note.PitchBend, note.PitchBendRange, note.Program,
            note.NoteCutTicks, note.NoteDelayTicks, note.Retrigger, note.PitchSlide, note.VolumeSlide,
            note.Volume, note.Modulation, note.Aftertouch, note.ReleaseVelocity, note.PitchBends, note.ControllerChanges,
            voiceClass.ToString().ToLowerInvariant());
    }

    private static string InstrumentFor(NoteEvent note, string requested)
    {
        if (note.Role == TrackRole.Drums) return PercussionName(note.Pitch);
        if (note.Role == TrackRole.Bass) return "bass";
        if (note.SourceTrack < 0 && note.Program == 0) return requested;
        return note.Program switch
        {
            >= 0 and <= 7 => "epiano",
            >= 8 and <= 15 => "bell",
            >= 16 and <= 23 => "organ",
            >= 24 and <= 31 => "pluck",
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
