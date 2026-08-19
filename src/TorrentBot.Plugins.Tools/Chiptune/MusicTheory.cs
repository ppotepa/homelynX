using System.Globalization;
using System.Text.RegularExpressions;

namespace TorrentBot.Plugins.Tools.Chiptune;

internal static partial class MusicTheory
{
    private static readonly IReadOnlyDictionary<string, int[]> Scales = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["major"]=[0,2,4,5,7,9,11], ["minor"]=[0,2,3,5,7,8,10], ["harmonic_minor"]=[0,2,3,5,7,8,11],
        ["melodic_minor"]=[0,2,3,5,7,9,11], ["pentatonic_major"]=[0,2,4,7,9], ["pentatonic_minor"]=[0,3,5,7,10],
        ["blues"]=[0,3,5,6,7,10], ["dorian"]=[0,2,3,5,7,9,10], ["phrygian"]=[0,1,3,5,7,8,10],
        ["lydian"]=[0,2,4,6,7,9,11], ["mixolydian"]=[0,2,4,5,7,9,10], ["locrian"]=[0,1,3,5,6,8,10],
        ["chromatic"]=[0,1,2,3,4,5,6,7,8,9,10,11], ["whole_tone"]=[0,2,4,6,8,10]
    };

    public static int[] GetScale(string name) => Scales.TryGetValue(name, out var scale)
        ? scale : throw new FormatException($"Unknown scale '{name}'. Available: {string.Join(", ", Scales.Keys)}.");

    public static int ParseKey(string key)
    {
        var match = KeyRegex().Match(key.Trim());
        if (!match.Success) throw new FormatException($"Invalid key '{key}'. Use C, F#, Bb, etc.");
        var pitch = match.Groups[1].Value.ToUpperInvariant()[0] switch { 'C'=>0,'D'=>2,'E'=>4,'F'=>5,'G'=>7,'A'=>9,'B'=>11,_=>0 };
        if (match.Groups[2].Value == "#") pitch++;
        if (match.Groups[2].Value == "b") pitch--;
        return (pitch + 12) % 12;
    }

    public static bool TryParsePitch(string text, out int pitch)
    {
        pitch = 0;
        var match = PitchRegex().Match(text.Trim());
        if (!match.Success || !int.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var octave)) return false;
        var semitone = ParseKey(match.Groups[1].Value + match.Groups[2].Value);
        pitch = (octave + 1) * 12 + semitone;
        return pitch is >= 0 and <= 127;
    }

    public static int DegreeToPitch(string degreeText, int root, int[] scale, int octave)
    {
        var match = DegreeRegex().Match(degreeText.Trim());
        if (!match.Success) throw new FormatException($"Invalid scale degree '{degreeText}'.");
        var accidental = match.Groups[1].Value switch { "b" => -1, "#" => 1, _ => 0 };
        var degree = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        if (degree < 1) throw new FormatException("Scale degrees start at 1.");
        var index = degree - 1;
        var pitch = (octave + 1) * 12 + root + scale[index % scale.Length] + 12 * (index / scale.Length) + accidental;
        if (pitch is < 0 or > 127) throw new FormatException($"Scale degree '{degreeText}' is outside MIDI range.");
        return pitch;
    }

    public static (int Low, int High) ResolveRange(ChiptuneSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.Range))
        {
            var parts = spec.Range.Split(':', 2);
            if (parts.Length != 2 || !TryParsePitch(parts[0], out var low) || !TryParsePitch(parts[1], out var high) || low > high)
                throw new FormatException("Invalid range. Use range=C3:C6.");
            return (low, high);
        }
        var root = ParseKey(spec.Key);
        var basePitch = (spec.Octave + 1) * 12 + root;
        return (basePitch, Math.Min(127, basePitch + spec.Octaves * 12 - 1));
    }

    [GeneratedRegex("^([A-Ga-g])([#b]?)$")] private static partial Regex KeyRegex();
    [GeneratedRegex("^([A-Ga-g])([#b]?)(-?\\d+)$")] private static partial Regex PitchRegex();
    [GeneratedRegex("^([#b]?)(\\d+)$")] private static partial Regex DegreeRegex();
}
