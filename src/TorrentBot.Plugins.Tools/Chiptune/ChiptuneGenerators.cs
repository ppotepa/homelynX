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
            "riff" => Riff(spec, scale, root, range, random),
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
        var all = ScalePitches(scale, root, range).ToArray();
        var notes = new List<NoteEvent>();
        var steps = spec.Bars * 16;
        var current = Math.Clamp(Array.FindIndex(all, x => x % 12 == root), 0, all.Length - 1);
        var direction = spec.Direction == "down" ? -1 : 1;
        for (var step = 0; step < steps; step++)
        {
            if (step % 4 == 0 || random.NextDouble() < Density(spec.Style))
            {
                var pitch = all[current] + spec.Transpose;
                notes.Add(new NoteEvent(step * TempoMap.Ppq / 4, TempoMap.Ppq / (step % 4 == 0 ? 2 : 4), ClampPitch(pitch), step % 4 == 0 ? 120 : 96, TrackRole.Lead));
            }
            current = NextIndex(current, all.Length, spec.Direction, ref direction, random);
        }

        var low = all.Where(x => x <= range.Low + 12).DefaultIfEmpty(all[0]).ToArray();
        for (var beat = 0; beat < spec.Bars * 4; beat++)
        {
            var bass = low[(beat / 4) % low.Length] - 12 + spec.Transpose;
            while (bass < 24) bass += 12;
            notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq * 3 / 4, ClampPitch(bass), 106, TrackRole.Bass));
            notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, beat % 4 == 0 ? 36 : 42, beat % 4 == 0 ? 118 : 78, TrackRole.Drums));
            if (beat % 4 is 1 or 3) notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, 38, 105, TrackRole.Drums));
        }

        if (spec.Style != "minimal")
        {
            var chord = all.Take(Math.Min(3, all.Length)).ToArray();
            for (var step = 0; step < steps; step++)
                notes.Add(new NoteEvent(step * TempoMap.Ppq / 4, TempoMap.Ppq / 4, ClampPitch(chord[step % chord.Length] + spec.Transpose), 70, TrackRole.Arp));
        }
        return notes.OrderBy(x => x.StartTick).ThenBy(x => x.Role).ToArray();
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

    private static int NextIndex(int current, int count, string mode, ref int direction, Random random)
    {
        var step = random.NextDouble() switch { < .15 => -2, < .4 => -1, < .55 => 0, < .85 => 1, _ => 2 };
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
    private static double Density(string style) => style switch { "boss" => .78, "jrpg" => .58, "minimal" => .3, _ => .48 };
    private static int ClampPitch(int pitch) => Math.Clamp(pitch, 0, 127);
}
