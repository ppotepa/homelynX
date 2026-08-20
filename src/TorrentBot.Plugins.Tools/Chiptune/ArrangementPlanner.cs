namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class ArrangementPlanner
{
    private readonly record struct SourcePartKey(int Track, int Channel, int Program, int Bank, TrackRole Role);
    private sealed record WindowData(int Index, double BaseScore, string Fingerprint, int HookCount);
    private sealed record WindowScore(int Index, double Score, int Recurrence, int HookCount);

    public static Song Plan(Song song, ChiptuneSpec spec)
    {
        var notes = song.Notes.ToArray();
        if (notes.Length == 0) return song;

        if (spec.Mode == ChiptuneMode.Midi)
        {
            notes = PromoteCounterLead(notes, song.MidiMetadata);
            notes = LabelMidiSections(notes);
        }

        if (spec.RegisterMode == "auto" && spec.Mode is ChiptuneMode.Midi or ChiptuneMode.Generate)
            notes = NormalizeRegisters(notes, spec);

        notes = notes.Select(note => note with
        {
            Patch = ResolvePatch(note, spec),
            Velocity = Math.Clamp(note.Velocity + (int)Math.Round((note.SectionIntensity - .55) * 14), 1, 127)
        }).OrderBy(x => x.StartTick).ThenBy(x => x.Role).ThenBy(x => x.Pitch).ToArray();

        return song with { Notes = notes };
    }

    internal static IReadOnlyDictionary<TrackRole,string> PaletteFor(string chip, string style)
    {
        var palette = style.ToLowerInvariant() switch
        {
            "happy" => Palette("lead", "bell", "bass", "soft_lead", "pluck"),
            "jrpg" => Palette("soft_lead", "bell", "bass", "strings", "pluck"),
            "boss" => Palette("brass", "lead", "bass", "strings", "pluck"),
            "dungeon" or "dark" => Palette("soft_lead", "bell", "bass", "pad", "pluck"),
            "menu" => Palette("epiano", "bell", "bass", "soft_lead", "pluck"),
            "space" => Palette("soft_lead", "bell", "bass", "pad", "pluck"),
            "racing" or "chipbreak" => Palette("lead", "pluck", "bass", "brass", "pluck"),
            "minimal" => Palette("soft_lead", "pluck", "bass", "pad", "pluck"),
            _ => Palette("lead", "pluck", "bass", "soft_lead", "pluck")
        };

        if (chip is "gb" or "gbc" or "nes" or "sms" or "pcspeaker" or "zx_spectrum")
        {
            palette[TrackRole.Harmony] = palette[TrackRole.Harmony] is "strings" or "pad" ? "soft_lead" : palette[TrackRole.Harmony];
            if (palette[TrackRole.Lead] == "epiano") palette[TrackRole.Lead] = "bell";
        }
        return palette;
    }

    internal static string DescribePalette(string chip, string style)
    {
        var palette = PaletteFor(chip, style);
        return string.Join(", ", new[] { TrackRole.Lead, TrackRole.CounterLead, TrackRole.Bass, TrackRole.Harmony, TrackRole.Arp }
            .Select(role => $"{role}={palette[role]}"));
    }

    private static Dictionary<TrackRole,string> Palette(string lead, string counter, string bass, string harmony, string arp) => new()
    {
        [TrackRole.Lead] = lead,
        [TrackRole.CounterLead] = counter,
        [TrackRole.Bass] = bass,
        [TrackRole.Harmony] = harmony,
        [TrackRole.Arp] = arp,
        [TrackRole.Drums] = "auto"
    };

    private static NoteEvent[] PromoteCounterLead(NoteEvent[] notes, MidiMetadata? metadata)
    {
        if (notes.Any(x => x.Role == TrackRole.CounterLead)) return notes;
        var names = metadata?.TrackNames ?? new Dictionary<int,string>();
        var candidates = notes
            .Where(x => x.Role == TrackRole.Harmony && x.SourceTrack >= 0 && x.SourceChannel != 9)
            .GroupBy(x => new SourcePartKey(x.SourceTrack, x.SourceChannel, x.Program, x.Bank, x.Role))
            .Select(group =>
            {
                var items = group.ToArray();
                var peak = PeakOverlap(items);
                var median = items.Select(x => x.Pitch).Order().ElementAt(items.Length / 2);
                var name = names.GetValueOrDefault(group.Key.Track, string.Empty).ToLowerInvariant();
                var score = (peak <= 1 ? 34 : peak == 2 ? 12 : -20) + Math.Clamp(median - 55, -10, 24);
                if (group.Key.Program is >= 80 and <= 87) score += 32;
                else if (group.Key.Program is >= 56 and <= 79 or >= 24 and <= 31) score += 18;
                else if (group.Key.Program is >= 0 and <= 7) score += 10;
                if (name.Contains("counter")) score += 50;
                if (name.Contains("melody") || name.Contains("lead") || name.Contains("solo") || name.Contains("theme")) score += 28;
                if (name.Contains("pad") || name.Contains("chord") || name.Contains("string")) score -= 20;
                return (group.Key, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .ToArray();
        if (candidates.Length == 0 || candidates[0].Score < 25) return notes;
        var selected = candidates[0].Key;
        return notes.Select(note => note.SourceTrack == selected.Track && note.SourceChannel == selected.Channel &&
                                         note.Program == selected.Program && note.Bank == selected.Bank && note.Role == TrackRole.Harmony
            ? note with { Role = TrackRole.CounterLead }
            : note).ToArray();
    }

    private static NoteEvent[] LabelMidiSections(NoteEvent[] notes)
    {
        if (notes.Any(x => !string.Equals(x.Section, "body", StringComparison.OrdinalIgnoreCase))) return notes;
        var endTick = notes.Max(x => x.EndTick);
        var window = TempoMap.Ppq * 4L;
        var windowCount = Math.Max(1, (int)Math.Ceiling(endTick / (double)window));
        if (windowCount == 1) return notes.Select(x => x with { Section = "body", SectionIntensity = .6 }).ToArray();

        var windows = Enumerable.Range(0, windowCount).Select(index =>
        {
            var start = index * window; var end = start + window;
            var items = notes.Where(x => x.StartTick >= start && x.StartTick < end).ToArray();
            if (items.Length == 0) return new WindowData(index, 0, string.Empty, 0);
            var avgVelocity = items.Average(x => x.Velocity);
            var melodic = items.Where(x => x.Role != TrackRole.Drums).ToArray();
            var avgPitch = melodic.Length == 0 ? 48 : melodic.Average(x => x.Pitch);
            var parts = items.Select(x => (x.SourceTrack, x.SourceChannel, x.Program)).Distinct().Count();
            var hooks = items.Count(x => x.Role is TrackRole.Lead or TrackRole.CounterLead);
            var drums = items.Count(x => x.Role == TrackRole.Drums);
            var fingerprint = HookFingerprint(items, start);
            var baseScore = items.Length * 4 + avgVelocity / 4 + avgPitch / 7 + parts * 8 + hooks * 3 + drums * 1.5;
            return new WindowData(index, baseScore, fingerprint, hooks);
        }).ToArray();

        var recurrences = windows
            .Where(x => x.Fingerprint.Length > 0)
            .GroupBy(x => x.Fingerprint, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var scores = windows.Select(windowData =>
        {
            var recurrence = windowData.Fingerprint.Length == 0 ? 0 : recurrences.GetValueOrDefault(windowData.Fingerprint, 1);
            var recurrenceBonus = recurrence >= 2 && windowData.HookCount >= 2 ? Math.Min(65, (recurrence - 1) * 28) : 0;
            return new WindowScore(windowData.Index, windowData.BaseScore + recurrenceBonus, recurrence, windowData.HookCount);
        }).ToArray();

        var nonZero = scores.Where(x => x.Score > 0).Select(x => x.Score).Order().ToArray();
        if (nonZero.Length == 0) return notes;
        var average = nonZero.Average();
        var threshold = nonZero[(int)Math.Floor((nonZero.Length - 1) * .72)];
        var min = nonZero[0]; var max = nonZero[^1];
        var labels = scores.ToDictionary(x => x.Index, x =>
        {
            var normalized = max <= min ? .65 : .38 + .62 * ((x.Score - min) / (max - min));
            var repeatedHook = x.Recurrence >= 2 && x.HookCount >= 2 && x.Score >= average * .86;
            var section = repeatedHook || (x.Score >= threshold && x.Score >= average * 1.03) ? "chorus" : "verse";
            if (x.Index == 0 && x.Score < average * .78) section = "intro";
            if (x.Index == windowCount - 1 && x.Score < average * .82) section = "outro";
            if (section == "chorus") normalized = Math.Max(normalized, .84);
            return (Section: section, Intensity: Math.Clamp(normalized, .3, 1.0));
        });

        return notes.Select(note =>
        {
            var index = Math.Clamp((int)(note.StartTick / window), 0, windowCount - 1);
            var label = labels[index];
            return note with { Section = label.Section, SectionIntensity = label.Intensity };
        }).ToArray();
    }

    private static string HookFingerprint(NoteEvent[] items, long windowStart)
    {
        var hook = items
            .Where(x => x.Role is TrackRole.Lead or TrackRole.CounterLead)
            .OrderBy(x => x.StartTick).ThenByDescending(x => x.Role == TrackRole.Lead).ThenByDescending(x => x.Pitch)
            .Take(16)
            .ToArray();
        if (hook.Length < 3) return string.Empty;
        var basePitch = hook[0].Pitch;
        var grid = Math.Max(1L, TempoMap.Ppq / 4);
        return string.Join(";", hook.Select(note =>
        {
            var position = (int)Math.Round((note.StartTick - windowStart) / (double)grid);
            var duration = Math.Max(1, (int)Math.Round(note.DurationTick / (double)grid));
            var relativePitch = note.Pitch - basePitch;
            return $"{position}:{relativePitch}:{duration}:{(note.Role == TrackRole.Lead ? 'L' : 'C')}";
        }));
    }

    private static NoteEvent[] NormalizeRegisters(NoteEvent[] notes, ChiptuneSpec spec)
    {
        var result = notes.ToArray();
        var indexed = result.Select((note, index) => (note, index))
            .Where(x => x.note.Role != TrackRole.Drums)
            .GroupBy(x => (x.note.SourceTrack, x.note.SourceChannel, x.note.Program, x.note.Role, x.note.Section));

        foreach (var group in indexed)
        {
            var items = group.ToArray();
            if (items.Length == 0) continue;
            var pitches = items.Select(x => x.note.Pitch).Order().ToArray();
            var median = pitches[pitches.Length / 2];
            var (low, high, target) = PreferredRange(spec.Chip, group.Key.Role, group.Key.Section, spec.ChorusLift);
            var minPitch = pitches[0]; var maxPitch = pitches[^1];
            var validShifts = Enumerable.Range(-3, 7).Select(x => x * 12)
                .Where(shift => minPitch + shift >= low && maxPitch + shift <= high)
                .ToArray();
            var shift = validShifts.Length > 0
                ? validShifts.OrderBy(x => Math.Abs((median + x) - target)).ThenBy(x => Math.Abs(x)).First()
                : (int)Math.Round((target - median) / 12d) * 12;
            foreach (var item in items)
            {
                var pitch = item.note.Pitch + shift;
                while (pitch < low && pitch + 12 <= high) pitch += 12;
                while (pitch > high && pitch - 12 >= low) pitch -= 12;
                result[item.index] = item.note with { Pitch = Math.Clamp(pitch, Math.Max(0, low), Math.Min(127, high)) };
            }
        }
        return result;
    }

    private static (int Low, int High, int Target) PreferredRange(string chip, TrackRole role, string section, int chorusLift)
    {
        var rich = chip is "snes" or "genesis" or "pce";
        var constrained = chip is "atari2600" or "pcspeaker" or "zx_spectrum";
        var range = role switch
        {
            TrackRole.Bass => (Low: 30, High: rich ? 60 : 55, Target: 43),
            TrackRole.Harmony => (Low: 43, High: rich ? 76 : 72, Target: 58),
            TrackRole.Arp => (Low: 52, High: rich ? 91 : 84, Target: 70),
            TrackRole.CounterLead => (Low: 55, High: rich ? 91 : 84, Target: 70),
            _ => (Low: constrained ? 48 : 55, High: rich ? 96 : 88, Target: 72)
        };
        if (section.Equals("chorus", StringComparison.OrdinalIgnoreCase) && role is TrackRole.Lead or TrackRole.CounterLead or TrackRole.Arp)
            range.Target = Math.Min(range.High - 3, range.Target + chorusLift);
        if (section.Equals("intro", StringComparison.OrdinalIgnoreCase) && role == TrackRole.Lead)
            range.Target = Math.Max(range.Low + 3, range.Target - 7);
        return range;
    }

    private static string ResolvePatch(NoteEvent note, ChiptuneSpec spec)
    {
        var roleOverride = note.Role switch
        {
            TrackRole.Lead => spec.LeadInstrument,
            TrackRole.CounterLead => spec.CounterInstrument,
            TrackRole.Bass => spec.BassInstrument,
            TrackRole.Harmony => spec.HarmonyInstrument,
            TrackRole.Arp => spec.ArpInstrument,
            TrackRole.Drums => spec.DrumsInstrument,
            _ => "auto"
        };
        if (!IsAuto(roleOverride)) return NormalizePatch(roleOverride);
        if (!IsAuto(spec.Instrument) && note.Role != TrackRole.Drums) return NormalizePatch(spec.Instrument);
        if (!IsAuto(note.Patch)) return NormalizePatch(note.Patch);
        if (note.Role == TrackRole.Drums) return PercussionPatch(note.Pitch);

        var palette = PaletteFor(spec.Chip, spec.Style);
        var patch = spec.Mode == ChiptuneMode.Midi && note.SourceTrack >= 0
            ? ProgramPatch(note.Program)
            : "auto";
        if (patch == "auto") patch = palette.GetValueOrDefault(note.Role, "lead");
        return AdaptAutoPatchForSection(patch, note, palette);
    }

    private static string AdaptAutoPatchForSection(string patch, NoteEvent note, IReadOnlyDictionary<TrackRole,string> palette)
    {
        if (note.Section.Equals("chorus", StringComparison.OrdinalIgnoreCase))
        {
            if (note.Role == TrackRole.Lead && patch is "pad" or "strings" or "organ" or "epiano" or "soft_lead")
                return palette.GetValueOrDefault(TrackRole.Lead, "lead");
            if (note.Role == TrackRole.CounterLead && patch is "pad" or "strings" or "organ" or "epiano" or "soft_lead")
                return palette.GetValueOrDefault(TrackRole.CounterLead, "bell");
            if (note.Role == TrackRole.Arp && patch is "pad" or "strings" or "organ")
                return palette.GetValueOrDefault(TrackRole.Arp, "pluck");
        }
        if (note.Section.Equals("intro", StringComparison.OrdinalIgnoreCase) && note.Role == TrackRole.Lead && patch is "lead" or "brass")
            return "soft_lead";
        return patch;
    }

    private static string ProgramPatch(int program) => program switch
    {
        >= 0 and <= 7 => "epiano",
        >= 8 and <= 15 => "bell",
        >= 16 and <= 23 => "organ",
        >= 24 and <= 31 => "pluck",
        >= 32 and <= 39 => "bass",
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
        _ => "auto"
    };

    private static string PercussionPatch(int pitch) => pitch switch
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

    private static string NormalizePatch(string patch) => patch.Equals("arp", StringComparison.OrdinalIgnoreCase) ? "pluck" : patch.ToLowerInvariant();
    private static bool IsAuto(string? patch) => string.IsNullOrWhiteSpace(patch) || patch.Equals("auto", StringComparison.OrdinalIgnoreCase);

    private static int PeakOverlap(IEnumerable<NoteEvent> notes)
    {
        var active = 0; var peak = 0;
        foreach (var point in notes.SelectMany(x => new[] { (x.StartTick, 1), (x.EndTick, -1) }).OrderBy(x => x.Item1).ThenBy(x => x.Item2))
        {
            active += point.Item2; peak = Math.Max(peak, active);
        }
        return peak;
    }
}
