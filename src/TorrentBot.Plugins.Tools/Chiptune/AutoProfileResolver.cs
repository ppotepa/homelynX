namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class AutoProfileResolver
{
    public static ChiptuneSpec Resolve(ChiptuneSpec spec, Song song)
    {
        if (spec.Mode != ChiptuneMode.Midi || spec.ChipExplicit) return spec;

        var peak = PeakOverlap(song.Notes);
        var hasDrums = song.Notes.Any(x => x.Role == TrackRole.Drums);
        var hasBass = song.Notes.Any(x => x.Role == TrackRole.Bass);
        var chip = peak >= 8 ? "snes" : peak >= 5 ? "genesis" : hasBass && hasDrums ? "nes" : "gbc";
        return spec with { Chip = chip };
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
