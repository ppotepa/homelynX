namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class ChiptuneGenerators
{
    private sealed record StyleProfile(double Density, double LeapChance, int StartOffset, int AccentEvery,
        long AccentLength, long StepLength, long BassLength, bool Drums, bool Hats, bool Snare, int KickEvery, bool Harmony);
    private sealed record SectionPlan(string Name, int StartBar, int Bars, double Intensity);

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
            "melody" => Melody(spec, scale, root, range, random),
            "song" => SongArrangement(spec, scale, root, range, random),
            "bassline" => Bassline(spec, scale, root, range),
            "drums" => Drums(spec),
            _ => throw new FormatException("generate must be scale, arp, riff, melody, song, bassline or drums.")
        };
        return new Song(notes, TempoMap.Fixed(spec.Bpm));
    }

    private static IReadOnlyList<NoteEvent> Scale(ChiptuneSpec spec, int[] scale, int root, (int Low, int High) range)
    {
        var pitches = ScalePitches(scale, root, range).ToArray();
        var sequence = DirectionSequence(pitches, spec.Direction, spec.Bars * 8, new Random(spec.Seed));
        return sequence.Select((pitch, i) => new NoteEvent(i * TempoMap.Ppq / 2, TempoMap.Ppq / 2, pitch + spec.Transpose, 108, TrackRole.Lead,
            Section: "theme", SectionIntensity: .62)).ToArray();
    }

    private static IReadOnlyList<NoteEvent> Arp(ChiptuneSpec spec, int[] scale, int root, (int Low, int High) range)
    {
        var all = ScalePitches(scale, root, range).ToArray();
        var tonic = all.FirstOrDefault(x => x % 12 == root, all[0]);
        var chord = new[] { tonic, NearestScalePitch(all, tonic, 2), NearestScalePitch(all, tonic, 4) }.Distinct().ToArray();
        var sequence = DirectionSequence(chord, spec.Direction, spec.Bars * 16, new Random(spec.Seed));
        return sequence.Select((pitch, i) => new NoteEvent(i * TempoMap.Ppq / 4, TempoMap.Ppq / 4, pitch + spec.Transpose, 105, TrackRole.Arp,
            Section: "theme", SectionIntensity: .65)).ToArray();
    }

    private static IReadOnlyList<NoteEvent> Melody(ChiptuneSpec spec, int[] scale, int root, (int Low, int High) range, Random random)
    {
        var all = ScalePitches(scale, root, range).ToArray();
        var motif = BuildMotif(all, root, random, 16);
        var notes = new List<NoteEvent>();
        var steps = spec.Bars * 16;
        for (var step = 0; step < steps; step++)
        {
            var phrase = step / 16;
            var index = motif[step % motif.Length];
            if (phrase % 2 == 1 && step % 16 is 6 or 14) index = Math.Clamp(index + 1, 0, all.Length - 1);
            if (step % 16 == 15) index = NearestTonicIndex(all, root, index);
            var accent = step % 4 == 0;
            if (!accent && random.NextDouble() > .72) continue;
            notes.Add(new NoteEvent(step * TempoMap.Ppq / 4, accent ? TempoMap.Ppq / 2 : TempoMap.Ppq / 4,
                ClampPitch(all[index] + spec.Transpose), accent ? 116 : 100, TrackRole.Lead,
                Section: "theme", SectionIntensity: .68));
        }
        return notes;
    }

    private static IReadOnlyList<NoteEvent> SongArrangement(ChiptuneSpec spec, int[] scale, int root, (int Low, int High) range, Random random)
    {
        var all = ScalePitches(scale, root, range).ToArray();
        var motif = BuildMotif(all, root, random, 16);
        var sections = BuildSections(spec.Bars);
        var progression = ProgressionFor(spec, all, root);
        var notes = new List<NoteEvent>();

        foreach (var section in sections)
        {
            AddSectionMelody(notes, spec, all, root, motif, section, random);
            AddSectionBass(notes, spec, all, progression, section);
            AddSectionHarmony(notes, spec, all, progression, section);
            AddSectionDrums(notes, spec, section);
        }

        return notes.OrderBy(x => x.StartTick).ThenBy(x => x.Role).ThenBy(x => x.Pitch).ToArray();
    }

    private static void AddSectionMelody(List<NoteEvent> notes, ChiptuneSpec spec, int[] all, int root, int[] motif, SectionPlan section, Random random)
    {
        var startStep = section.StartBar * 16;
        var steps = section.Bars * 16;
        var isChorus = section.Name == "chorus";
        var isIntro = section.Name == "intro";
        var isOutro = section.Name == "outro";
        var density = ProfileForStyle(spec.Style).Density * (.72 + section.Intensity * .42);

        for (var localStep = 0; localStep < steps; localStep++)
        {
            var globalStep = startStep + localStep;
            var motifPosition = localStep % motif.Length;
            var index = motif[motifPosition];
            if (isChorus) index = Math.Clamp(index + 2 + (motifPosition is 4 or 12 ? 1 : 0), 0, all.Length - 1);
            if (isIntro) index = Math.Max(0, index - 1);
            if (isOutro && localStep >= Math.Max(0, steps - 4)) index = NearestTonicIndex(all, root, index);
            if (motifPosition == 15) index = NearestTonicIndex(all, root, index);

            var accent = localStep % 4 == 0;
            var phraseAccent = localStep % 16 is 0 or 8;
            if (!accent && random.NextDouble() > density) continue;
            var duration = phraseAccent ? TempoMap.Ppq / 2 : accent ? TempoMap.Ppq * 3 / 8 : TempoMap.Ppq / 4;
            var velocity = Math.Clamp((int)Math.Round(94 + section.Intensity * 26 + (phraseAccent ? 5 : 0)), 1, 127);
            notes.Add(new NoteEvent(globalStep * TempoMap.Ppq / 4, duration,
                ClampPitch(all[index] + spec.Transpose), velocity, TrackRole.Lead,
                Section: section.Name, SectionIntensity: section.Intensity));

            // A chorus should sound like a real hook: a separate counter-line
            // answers the lead on off-beats instead of being folded into Harmony.
            if (isChorus && localStep % 4 == 2 && section.Intensity >= .82)
            {
                var counterIndex = Math.Clamp(index - 2 + ((localStep / 4) % 2), 0, all.Length - 1);
                notes.Add(new NoteEvent(globalStep * TempoMap.Ppq / 4, TempoMap.Ppq / 2,
                    ClampPitch(all[counterIndex] + spec.Transpose), Math.Clamp(78 + (int)(section.Intensity * 20), 1, 118), TrackRole.CounterLead,
                    Section: section.Name, SectionIntensity: section.Intensity));
            }
        }
    }

    private static void AddSectionBass(List<NoteEvent> notes, ChiptuneSpec spec, int[] all, int[] progression, SectionPlan section)
    {
        for (var localBar = 0; localBar < section.Bars; localBar++)
        {
            var globalBar = section.StartBar + localBar;
            var root = progression[globalBar % progression.Length];
            for (var beat = 0; beat < 4; beat++)
            {
                var pitch = root;
                while (pitch > 52) pitch -= 12;
                while (pitch < 28) pitch += 12;
                if (section.Name == "chorus" && beat is 1 or 3)
                    pitch = NearestScalePitch(all, pitch, beat == 1 ? 2 : 4);
                notes.Add(new NoteEvent((globalBar * 4L + beat) * TempoMap.Ppq,
                    section.Name == "chorus" ? TempoMap.Ppq * 5 / 8 : TempoMap.Ppq * 3 / 4,
                    ClampPitch(pitch + spec.Transpose), Math.Clamp(92 + (int)(section.Intensity * 24), 1, 122), TrackRole.Bass,
                    Section: section.Name, SectionIntensity: section.Intensity));
            }
        }
    }

    private static void AddSectionHarmony(List<NoteEvent> notes, ChiptuneSpec spec, int[] all, int[] progression, SectionPlan section)
    {
        if (section.Intensity < .42) return;
        var arpStride = section.Name == "chorus" ? 2 : 4;
        for (var localBar = 0; localBar < section.Bars; localBar++)
        {
            var globalBar = section.StartBar + localBar;
            var chordRoot = progression[globalBar % progression.Length];
            var chord = ChordTones(all, chordRoot);
            for (var step = 0; step < 16; step += arpStride)
            {
                var pitch = chord[(step / arpStride + globalBar) % chord.Length];
                while (pitch < 52) pitch += 12;
                while (pitch > 84) pitch -= 12;
                notes.Add(new NoteEvent((globalBar * 16L + step) * TempoMap.Ppq / 4,
                    arpStride == 2 ? TempoMap.Ppq / 4 : TempoMap.Ppq / 2,
                    ClampPitch(pitch + spec.Transpose), Math.Clamp(58 + (int)(section.Intensity * 24), 1, 96), TrackRole.Arp,
                    Section: section.Name, SectionIntensity: section.Intensity));
            }
            if (section.Name == "chorus" && section.Intensity >= .9)
            {
                var support = chord.Length > 1 ? chord[1] : chord[0];
                while (support < 48) support += 12;
                while (support > 72) support -= 12;
                notes.Add(new NoteEvent(globalBar * 4L * TempoMap.Ppq, TempoMap.Ppq * 2,
                    ClampPitch(support + spec.Transpose), 68, TrackRole.Harmony,
                    Section: section.Name, SectionIntensity: section.Intensity));
            }
        }
    }

    private static void AddSectionDrums(List<NoteEvent> notes, ChiptuneSpec spec, SectionPlan section)
    {
        if (section.Name == "intro" && section.Intensity < .4) return;
        var style = ProfileForStyle(spec.Style);
        if (!style.Drums) return;
        for (var localBar = 0; localBar < section.Bars; localBar++)
        {
            var globalBar = section.StartBar + localBar;
            for (var eighth = 0; eighth < 8; eighth++)
            {
                var tick = (globalBar * 8L + eighth) * TempoMap.Ppq / 2;
                if (eighth is 0 or 4)
                    notes.Add(new NoteEvent(tick, TempoMap.Ppq / 8, 36, 118, TrackRole.Drums, Section: section.Name, SectionIntensity: section.Intensity));
                if (eighth is 2 or 6)
                    notes.Add(new NoteEvent(tick, TempoMap.Ppq / 8, 38, 106, TrackRole.Drums, Section: section.Name, SectionIntensity: section.Intensity));
                if (style.Hats && (section.Name == "chorus" || eighth % 2 == 1))
                    notes.Add(new NoteEvent(tick, TempoMap.Ppq / 8, eighth == 7 && section.Name == "chorus" ? 46 : 42,
                        section.Name == "chorus" ? 88 : 72, TrackRole.Drums, Section: section.Name, SectionIntensity: section.Intensity));
            }
            if (section.Name == "chorus" && localBar == 0)
                notes.Add(new NoteEvent(globalBar * 4L * TempoMap.Ppq, TempoMap.Ppq / 4, 49, 100, TrackRole.Drums,
                    Section: section.Name, SectionIntensity: section.Intensity));
        }
    }

    private static IReadOnlyList<SectionPlan> BuildSections(int bars)
    {
        if (bars <= 4) return [new("theme", 0, bars, .68)];
        if (bars <= 7)
        {
            var verse = Math.Max(2, bars / 2);
            return [new("verse", 0, verse, .58), new("chorus", verse, bars - verse, .92)];
        }
        if (bars <= 11)
        {
            var verse = Math.Max(2, (bars - 2) / 2);
            var chorus = Math.Max(2, bars - verse - 2);
            return [new("intro", 0, 1, .36), new("verse", 1, verse, .58), new("chorus", 1 + verse, chorus, .92), new("outro", 1 + verse + chorus, bars - 1 - verse - chorus, .44)];
        }

        var introBars = Math.Max(1, bars / 8);
        var outroBars = 1;
        var remaining = bars - introBars - outroBars;
        var verse1 = Math.Max(2, remaining / 4);
        var chorus1 = Math.Max(2, remaining / 4);
        var verse2 = Math.Max(1, remaining / 6);
        var chorus2 = remaining - verse1 - chorus1 - verse2;
        if (chorus2 < 2)
        {
            var need = 2 - chorus2;
            verse1 = Math.Max(2, verse1 - need);
            chorus2 = remaining - verse1 - chorus1 - verse2;
        }
        var cursor = 0;
        var result = new List<SectionPlan>
        {
            new("intro", cursor, introBars, .35)
        };
        cursor += introBars;
        result.Add(new("verse", cursor, verse1, .57)); cursor += verse1;
        result.Add(new("chorus", cursor, chorus1, .90)); cursor += chorus1;
        result.Add(new("verse", cursor, verse2, .64)); cursor += verse2;
        result.Add(new("chorus", cursor, chorus2, 1.0)); cursor += chorus2;
        result.Add(new("outro", cursor, outroBars, .46));
        return result;
    }

    private static int[] BuildMotif(int[] all, int root, Random random, int length)
    {
        var tonic = all.Select((pitch, index) => (pitch, index))
            .Where(x => x.pitch % 12 == root)
            .OrderBy(x => Math.Abs(x.pitch - 67))
            .Select(x => x.index)
            .FirstOrDefault(Math.Clamp(all.Length / 2, 0, all.Length - 1));
        var motif = new int[length];
        var current = tonic;
        var direction = 1;
        for (var i = 0; i < length; i++)
        {
            if (i == 0 || i == length - 1) current = tonic;
            else if (i == length / 2) current = Math.Clamp(tonic + 4, 0, all.Length - 1);
            else
            {
                var leap = random.NextDouble() < .14;
                var step = leap ? (random.Next(2) == 0 ? -3 : 3) : random.Next(-1, 3);
                if (i % 4 == 0) step = Math.Abs(step) * direction;
                current += step;
                if (current < 0 || current >= all.Length)
                {
                    direction *= -1;
                    current = Math.Clamp(current, 0, all.Length - 1);
                }
            }
            motif[i] = current;
        }
        return motif;
    }

    private static int NearestTonicIndex(int[] all, int root, int around) => all
        .Select((pitch, index) => (pitch, index))
        .Where(x => x.pitch % 12 == root)
        .OrderBy(x => Math.Abs(x.index - around))
        .Select(x => x.index)
        .FirstOrDefault(Math.Clamp(around, 0, all.Length - 1));

    private static int[] ProgressionFor(ChiptuneSpec spec, int[] all, int root)
    {
        if (!string.IsNullOrWhiteSpace(spec.Progression)) return ParseProgression(spec.Progression, all, root);
        var degrees = spec.Style switch
        {
            "happy" => new[] { 1, 5, 6, 4 },
            "jrpg" => new[] { 1, 6, 4, 5 },
            "boss" => new[] { 1, 6, 7, 5 },
            "dungeon" or "dark" => new[] { 1, 6, 3, 7 },
            "menu" => new[] { 1, 4, 2, 5 },
            _ => new[] { 1, 5, 4, 5 }
        };
        var tonic = all.Where(x => x % 12 == root).OrderBy(x => Math.Abs(x - 48)).FirstOrDefault(all[0]);
        return degrees.Select(degree => DegreeNear(all, tonic, degree - 1)).ToArray();
    }

    private static int DegreeNear(int[] all, int tonic, int offset)
    {
        var tonicIndex = Array.IndexOf(all, all.Where(x => x % 12 == tonic % 12).OrderBy(x => Math.Abs(x - tonic)).First());
        return all[Math.Clamp(tonicIndex + offset, 0, all.Length - 1)];
    }

    private static int[] ChordTones(int[] all, int root)
    {
        var rootPitch = all.OrderBy(x => Math.Abs(x - root)).First();
        var index = Array.IndexOf(all, rootPitch);
        return new[] { index, index + 2, index + 4 }.Select(i => all[Math.Clamp(i, 0, all.Length - 1)]).Distinct().ToArray();
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
                notes.Add(new NoteEvent(step * TempoMap.Ppq / 4, duration, ClampPitch(pitch), step % profile.AccentEvery == 0 ? 120 : 96, TrackRole.Lead,
                    Section: "riff", SectionIntensity: .66));
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
            notes.Add(new NoteEvent(beat * TempoMap.Ppq, profile.BassLength, ClampPitch(bass), 106, TrackRole.Bass, Section: "riff", SectionIntensity: .62));
            if (profile.Drums)
            {
                if (beat % profile.KickEvery == 0) notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, 36, 118, TrackRole.Drums, Section: "riff", SectionIntensity: .65));
                else if (profile.Hats) notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, 42, 78, TrackRole.Drums, Section: "riff", SectionIntensity: .65));
                if (profile.Snare && beat % 4 is 1 or 3) notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, 38, 105, TrackRole.Drums, Section: "riff", SectionIntensity: .65));
            }
        }

        if (profile.Harmony)
        {
            var chord = all.Take(Math.Min(3, all.Length)).ToArray();
            for (var step = 0; step < steps; step++)
                notes.Add(new NoteEvent(step * TempoMap.Ppq / 4, TempoMap.Ppq / 4, ClampPitch(chord[step % chord.Length] + spec.Transpose), 70, TrackRole.Arp, Section: "riff", SectionIntensity: .55));
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
            notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq * 3 / 4, pitch, 112, TrackRole.Bass, Section: "theme", SectionIntensity: .62));
        }
        return notes;
    }

    private static IReadOnlyList<NoteEvent> Drums(ChiptuneSpec spec)
    {
        var notes = new List<NoteEvent>();
        for (var beat = 0; beat < spec.Bars * 4; beat++)
        {
            notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, beat % 4 == 0 ? 36 : 42, beat % 4 == 0 ? 120 : 78, TrackRole.Drums, Section: "theme", SectionIntensity: .68));
            if (beat % 4 is 1 or 3) notes.Add(new NoteEvent(beat * TempoMap.Ppq, TempoMap.Ppq / 8, 38, 105, TrackRole.Drums, Section: "theme", SectionIntensity: .68));
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
        var nearest = all.Select((pitch, index) => (pitch, index)).OrderBy(x => Math.Abs(x.pitch - tonic)).First();
        return all[Math.Clamp(nearest.index + degreeOffset, 0, all.Length - 1)];
    }

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
