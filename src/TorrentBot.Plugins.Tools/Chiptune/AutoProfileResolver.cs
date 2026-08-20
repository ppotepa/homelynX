namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class AutoProfileResolver
{
    private static readonly string[] AutomaticTargets =
        ["snes", "genesis", "pce", "nes", "gbc", "sms", "c64_8580"];

    public static ChiptuneSpec Resolve(ChiptuneSpec spec, Song song)
    {
        if (spec.ChipExplicit || spec.Mode is not (ChiptuneMode.Midi or ChiptuneMode.Generate)) return spec;

        var scored = Rank(spec, song);
        if (scored.Count == 0)
            return spec with { Chip = spec.Mode == ChiptuneMode.Generate ? "snes" : "genesis" };
        return spec with { Chip = scored[0].Chip };
    }

    internal static IReadOnlyList<(string Chip, int Score)> Rank(ChiptuneSpec spec, Song song)
        => AutomaticTargets
            .Select(chip => TryScore(chip, spec, song))
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Array.IndexOf(AutomaticTargets, x.Chip))
            .ToArray();

    private static (string Chip, int Score)? TryScore(string chip, ChiptuneSpec spec, Song song)
    {
        try
        {
            var candidate = spec with { Chip = chip, ChipExplicit = true };
            var planned = ArrangementPlanner.Plan(song, candidate);
            var hardware = VoiceAllocator.Allocate(planned, candidate);
            var sourceCount = Math.Max(1, planned.Notes.Count);
            var peak = PeakOverlap(planned.Notes);
            var voices = ChipProfile.For(chip).Voices.Count;
            var programs = song.Notes
                .Where(x => x.SourceChannel >= 0 && x.SourceChannel != 9)
                .Select(x => x.Program)
                .Distinct()
                .ToArray();
            var patches = planned.Notes
                .Where(x => x.Role != TrackRole.Drums)
                .Select(x => x.Patch)
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != "auto")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var hasDrums = planned.Notes.Any(x => x.Role == TrackRole.Drums);
            var hooks = planned.Notes.Count(x => x.Role is TrackRole.Lead or TrackRole.CounterLead);

            var score = 10_000;
            score -= hardware.DroppedNotes * 6_000 / sourceCount;
            score -= hardware.RevoicedNotes * 900 / sourceCount;
            score -= hardware.ArpeggiatedNotes * 500 / sourceCount;
            score += Math.Min(peak, voices) * 20;
            if (voices >= peak) score += 120;
            score += Math.Min(120, hooks * 2);

            var diversity = programs.Length > 0 ? programs.Length : patches.Length;
            score += chip switch
            {
                "snes" => 80 + diversity * 8,
                "genesis" => 70 + diversity * 7,
                "pce" => 45 + diversity * 4,
                "nes" => 25 + (hasDrums ? 35 : 0),
                "gbc" => 18 + (hasDrums ? 12 : 0),
                "sms" => 15 + (hasDrums ? 10 : 0),
                "c64_8580" => 10,
                _ => 0
            };

            var fmFriendly = programs.Length > 0
                ? programs.Count(program => program is >= 0 and <= 39 or >= 56 and <= 87)
                : patches.Count(patch => patch is "lead" or "soft_lead" or "bass" or "pluck" or "bell" or "brass" or "organ" or "epiano" or "reed" or "flute");
            if (chip == "genesis") score += fmFriendly * 5;

            if (peak <= 2 && diversity <= 2)
            {
                score += chip switch
                {
                    "gbc" => 170,
                    "nes" => 140,
                    "sms" => 120,
                    "pce" => 70,
                    "genesis" => 20,
                    _ => 0
                };
            }

            return (chip, score);
        }
        catch
        {
            return null;
        }
    }

    private static int PeakOverlap(IReadOnlyList<NoteEvent> notes)
    {
        var active = 0;
        var peak = 0;
        foreach (var point in notes.SelectMany(x => new[]
                     { (Tick: x.StartTick, Delta: 1), (Tick: x.EndTick, Delta: -1) })
                 .OrderBy(x => x.Tick).ThenBy(x => x.Delta))
        {
            active += point.Delta;
            peak = Math.Max(peak, active);
        }
        return peak;
    }
}
