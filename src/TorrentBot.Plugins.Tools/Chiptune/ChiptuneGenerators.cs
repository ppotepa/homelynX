namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class ChiptuneGenerators
{
    public static Song Generate(ChiptuneSpec spec)
    {
        var scale = MusicTheory.GetScale(spec.Scale);
        var root = MusicTheory.ParseKey(spec.Key);
        var range = MusicTheory.ResolveRange(spec);
        var random = new Random(spec.Seed);
        var notes = spec.Generate switch
        {
            "scale" => Scale(spec, scale, root, range),
            "arp" => Arp(spec, scale, root, range),
            "riff" or "song" => Riff(spec, scale, root, range, random),
            "bassline" => Bassline(spec, scale, root, range),
            "drums" => Drums(spec),
            _ => throw new FormatException("generate must be scale, arp or riff.")
        };
        return new Song(notes, TempoMap.Fixed(spec.Bpm));
    }

    private static IReadOnlyList<NoteEvent> Scale(ChiptuneSpec spec, int[] scale, int root, (int Low, int High) range)
    {
        var pitches = ScalePitches(scale, root, range).ToArray();
        var sequence = DirectionSequence(pitches, spec.Direction, spec.Bars * 8, new Random(spec.Seed));
        return sequence.Select((pitch, i) => new NoteEvent(i * TempoMap.Ppq / 2, TempoMap.Ppq / 2, pitch + spec.Transpose, 108, TrackRole.Lead)).ToArray();
    }

    private static IReadOnlyList<NoteEvent> Arp(ChiptuneSpec spec, int[] scale, int root, (int Low, int High) range)
    {
        var all = ScalePitches(scale, root, range).ToArray();
        var tonic = all.FirstOrDefault(x => x % 12 == root, all[0]);
        var chord = new[] { tonic, NearestScalePitch(all, tonic, 2), NearestScalePitch(all, tonic, 4) }.Distinct().ToArray();
        var sequence = DirectionSequence(chord, spec.Direction, spec.Bars * 16, new Random(spec.Seed));
        return sequence.Select((pitch, i) => new NoteEvent(i * TempoMap.Ppq / 4, TempoMap.Ppq / 4, pitch + spec.Transpose, 105, TrackRole.Arp)).ToArray();
    }

    private static IReadOnlyList<NoteEvent> Riff(ChiptuneSpec spec, int[] scale, int root, (int Low, int High) range, Random random)
    {
        var profile = ProfileForStyle(spec.Style);
        var all = ScalePitches(scale, root, range).ToArray();
        var notes = new List<NoteEvent>();
        var steps = spec.Bars * 16;
        var tonicIndex = Math.Clamp(Array.FindIndex(all, x => x % 12 == root), 0, all.Length - 1);
        var current = Math.Clamp(tonicIndex + profile.StartOffset, 0, all.Length - 1);
        var direction = spec.Direction == "down" ? -1 : 1;
        for (var step = 0; step < steps; step++)
        {
            if (step % profile.AccentEvery == 0 || random.NextDouble() < profile.Density)
            {
                var pitch = all[current] + spec.Transpose;
                var duration = step % profile.AccentEvery == 0 ? profile.AccentLength : profile.StepLength;
                notes.Add(new NoteEvent(step * TempoMap.Ppq / 4, duration, ClampPitch(pitch), step % profile.AccentEvery == 0 ? 120 : 96, TrackRole.Lead));
            }
            current = NextIndex(current, all.Length, spec.Direction, ref direction, random, profile.LeapChance);
        }

        var low = all.Where(x => x <= range.Low + 12).DefaultIfEmpty(all[0]).ToArray();
        var progression = ParseProgression(spec.Progression, all, root);
        for (var beat = 0; beat < spec.Bars * 4; beat++)
        {
            var barRoot = progression[(beat / 4) % progression.Length];
            var bass = (barRoot >= range.Low ? barRoot : low[(beat / 4) % low.Length]) - 12 + spec.Transpose;
            while (bass < 24) bass += 12;
            notes.Add(new NoteEvent(beat * TempoMap.Ppq, profile.BassLength, ClampPitch(bass), 106, TrackRole.Bass));
            if (profile.Drums)
            {
                if (beat % profile.KickEvery == 0) notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, 36, 118, TrackRole.Drums));
                else if (profile.Hats) notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, 42, 78, TrackRole.Drums));
                if (profile.Snare && beat % 4 is 1 or 3) notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, 38, 105, TrackRole.Drums));
            }
        }

        if (profile.Harmony)
        {
            var chord = all.Take(Math.Min(3, all.Length)).ToArray();
            for (var step = 0; step < steps; step++)
                notes.Add(new NoteEvent(step * TempoMap.Ppq / 4, TempoMap.Ppq / 4, ClampPitch(chord[step % chord.Length] + spec.Transpose), 70, TrackRole.Arp));
        }
        return notes.OrderBy(x => x.StartTick).ThenBy(x => x.Role).ToArray();
    }

    private static int[] ParseProgression(string? text, int[] scalePitches, int root)
    {
        if (string.IsNullOrWhiteSpace(text)) return [scalePitches.FirstOrDefault(x => x % 12 == root, scalePitches[0])];
        var result = new List<int>();
        foreach (var token in text.Split([' ', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (MusicTheory.TryParsePitch(token, out var absolute)) { result.Add(absolute); continue; }
            var roman = token.TrimStart('b', '#').ToLowerInvariant();
            var degree = roman switch { "i" => 1, "ii" => 2, "iii" => 3, "iv" => 4, "v" => 5, "vi" => 6, "vii" => 7, _ => 0 };
            if (degree == 0) throw new FormatException($"Invalid progression chord '{token}'. Use i VI III VII or C4.");
            var accidental = token.StartsWith("b", StringComparison.OrdinalIgnoreCase) ? -1 : token.StartsWith("#") ? 1 : 0;
            var index = Math.Min(scalePitches.Length - 1, degree - 1);
            result.Add(scalePitches[index] + accidental);
        }
        return result.Count == 0 ? [scalePitches[0]] : result.ToArray();
    }

    private static IReadOnlyList<NoteEvent> Bassline(ChiptuneSpec spec, int[] scale, int root, (int Low, int High) range)
    {
        var all = ScalePitches(scale, root, range).Where(x => x <= range.Low + 12).ToArray();
        if (all.Length == 0) all = [range.Low];
        var notes = new List<NoteEvent>();
        for (var beat = 0; beat < spec.Bars * 4; beat++)
        {
            var pitch = Math.Clamp(all[(beat + (beat / 4)) % all.Length] + spec.Transpose - 12, 0, 127);
            notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq * 3 / 4, pitch, 112, TrackRole.Bass));
        }
        return notes;
    }

    private static IReadOnlyList<NoteEvent> Drums(ChiptuneSpec spec)
    {
        var notes = new List<NoteEvent>();
        for (var beat = 0; beat < spec.Bars * 4; beat++)
        {
            notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, beat % 4 == 0 ? 36 : 42, beat % 4 == 0 ? 120 : 78, TrackRole.Drums));
            if (beat % 4 is 1 or 3) notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, 38, 105, TrackRole.Drums));
        }
        return notes;
    }

    private static IEnumerable<int> ScalePitches(int[] scale, int root, (int Low, int High) range)
    {
        for (var pitch = range.Low; pitch <= range.High; pitch++)
            if (scale.Contains((pitch - root + 120) % 12)) yield return pitch;
    }

    private static IEnumerable<int> DirectionSequence(int[] pitches, string direction, int count, Random random)
    {
        var index = direction == "down" ? pitches.Length - 1 : 0; var delta = direction == "down" ? -1 : 1;
        for (var i = 0; i < count; i++)
        {
            yield return pitches[index];
            if (direction == "random_walk") index = Math.Clamp(index + random.Next(-2, 3), 0, pitches.Length - 1);
            else
            {
                index += delta;
                if (index >= pitches.Length || index < 0)
                {
                    if (direction == "updown") { delta *= -1; index = Math.Clamp(index + 2 * delta, 0, pitches.Length - 1); }
                    else index = delta > 0 ? 0 : pitches.Length - 1;
                }
            }
        }
    }

    private static int NextIndex(int current, int count, string mode, ref int direction, Random random, double leapChance)
    {
        var step = random.NextDouble() < leapChance
            ? (random.Next(2) == 0 ? -3 : 3)
            : random.NextDouble() switch { < .15 => -2, < .4 => -1, < .55 => 0, < .85 => 1, _ => 2 };
        if (mode == "up") step = Math.Abs(step); else if (mode == "down") step = -Math.Abs(step); else if (mode == "updown") step = Math.Abs(step) * direction;
        var next = current + step;
        if (next < 0 || next >= count) { direction *= -1; next = Math.Clamp(current - step, 0, count - 1); }
        return next;
    }

    private static int NearestScalePitch(int[] all, int tonic, int degreeOffset)
    {
        var index = Array.IndexOf(all, tonic);
        return all[Math.Min(all.Length - 1, index + degreeOffset)];
    }
    private sealed record StyleProfile(double Density, double LeapChance, int StartOffset, int AccentEvery,
        long AccentLength, long StepLength, long BassLength, bool Drums, bool Hats, bool Snare, int KickEvery, bool Harmony);

    private static StyleProfile ProfileForStyle(string style) => style switch
    {
        "boss" => new(.82, .30, 2, 2, TempoMap.Ppq / 2, TempoMap.Ppq / 4, TempoMap.Ppq / 2, true, true, true, 2, true),
        "jrpg" => new(.62, .12, 0, 4, TempoMap.Ppq / 2, TempoMap.Ppq / 2, TempoMap.Ppq * 3 / 4, true, true, true, 4, true),
        "dungeon" => new(.45, .08, -2, 4, TempoMap.Ppq * 3 / 4, TempoMap.Ppq / 4, TempoMap.Ppq, true, true, false, 4, true),
        "menu" => new(.32, .05, 3, 8, TempoMap.Ppq, TempoMap.Ppq / 2, TempoMap.Ppq, false, false, false, 4, true),
        "racing" => new(.76, .18, 1, 2, TempoMap.Ppq / 4, TempoMap.Ppq / 4, TempoMap.Ppq / 2, true, true, true, 2, true),
        "space" => new(.36, .20, 4, 8, TempoMap.Ppq, TempoMap.Ppq / 2, TempoMap.Ppq * 3 / 4, false, false, false, 4, true),
        "dark" => new(.48, .25, -3, 4, TempoMap.Ppq * 3 / 4, TempoMap.Ppq / 4, TempoMap.Ppq, true, false, true, 4, false),
        "happy" => new(.70, .10, 1, 4, TempoMap.Ppq / 2, TempoMap.Ppq / 4, TempoMap.Ppq / 2, true, true, true, 4, true),
        "chipbreak" => new(.90, .38, 0, 2, TempoMap.Ppq / 4, TempoMap.Ppq / 8, TempoMap.Ppq / 4, true, true, true, 2, true),
        "minimal" => new(.25, .03, 0, 8, TempoMap.Ppq, TempoMap.Ppq / 2, TempoMap.Ppq, false, false, false, 4, false),
        _ => new(.50, .15, 0, 4, TempoMap.Ppq / 2, TempoMap.Ppq / 4, TempoMap.Ppq * 3 / 4, true, true, true, 4, true)
    };
    private static int ClampPitch(int pitch) => Math.Clamp(pitch, 0, 127);
}
