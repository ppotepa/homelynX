namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class AutoProfileResolver
{
    private static readonly string[] AutomaticTargets =
        ["snes", "genesis", "pce", "nes", "gbc", "sms", "c64_8580"];

    public static ChiptuneSpec Resolve(ChiptuneSpec spec, Song song)
    {
        if (spec.Mode != ChiptuneMode.Midi || spec.ChipExplicit) return spec;

        var scored = AutomaticTargets
            .Select(chip => TryScore(chip, spec, song))
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Array.IndexOf(AutomaticTargets, x.Chip))
            .ToArray();

        if (scored.Length == 0)
            return spec with { Chip = "genesis" };
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
            var hardware = VoiceAllocator.Allocate(song, candidate);
            var sourceCount = Math.Max(1, song.Notes.Count);
            var peak = PeakOverlap(song.Notes);
            var voices = ChipProfile.For(chip).Voices.Count;
            var programs = song.Notes
                .Where(x => x.SourceChannel >= 0 && x.SourceChannel != 9)
                .Select(x => x.Program)
                .Distinct()
                .ToArray();
            var hasDrums = song.Notes.Any(x => x.Role == TrackRole.Drums);

            // Retaining recognizable note onsets dominates all secondary
            // considerations. Revoicing and arpeggiation are smaller penalties:
            // they preserve musical information but change the source texture.
            var score = 10_000;
            score -= hardware.DroppedNotes * 6_000 / sourceCount;
            score -= hardware.RevoicedNotes * 900 / sourceCount;
            score -= hardware.ArpeggiatedNotes * 500 / sourceCount;

            // Prefer enough native voices for the source's real overlap, but do
            // not make raw channel count the only criterion.
            score += Math.Min(peak, voices) * 20;
            if (voices >= peak) score += 120;

            // Timbre-fit tie breakers. Sample/FM/wavetable machines can retain a
            // multi-program GM arrangement more convincingly than pulse-only
            // targets, while NES gets a useful percussion bonus from DPCM.
            var diversity = programs.Length;
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

            var fmFriendly = programs.Count(program =>
                program is >= 0 and <= 39 or >= 56 and <= 87);
            if (chip == "genesis") score += fmFriendly * 5;

            // For very small monophonic/simple MIDI, avoid automatically using
            // a heavyweight sample target when a simpler chip preserves the
            // score equally well.
            if (peak <= 2 && diversity <= 2)
            {
                score += chip switch
                {
                    "gbc" => 70,
                    "nes" => 60,
                    "sms" => 50,
                    "pce" => 30,
                    _ => 0
                };
            }

            return (chip, score);
        }
        catch
        {
            // An unsupported arrangement is not a fatal auto-selection error;
            // it simply removes this target from consideration. Explicit chip
            // requests still surface their real error through the normal path.
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
