using System.Globalization;
using System.Text.RegularExpressions;

namespace TorrentBot.Plugins.Tools.Chiptune;

internal static partial class ChiptuneParser
{
    private static readonly string[] Chips = ["gb", "gbc", "nes", "snes", "sms", "c64_6581", "c64_8580", "genesis", "pce", "atari2600", "pokey", "pcspeaker", "zx_spectrum"];
    private static readonly string[] Instruments = ["auto", "lead", "soft_lead", "bass", "pluck", "arp", "bell", "brass", "organ", "epiano", "strings", "pad", "reed", "flute", "drums", "kick", "snare", "hat", "open_hat", "tom", "crash", "ride"];
    private static readonly string[] Styles = ["arcade", "jrpg", "boss", "dungeon", "menu", "racing", "space", "dark", "happy", "chipbreak", "minimal"];
    private static readonly string[] Formats = ["wav", "mp3", "ogg", "flac"];
    private static readonly string[] GenerateModes = ["scale", "arp", "riff", "melody", "song", "bassline", "drums"];

    public static ChiptuneSpec Parse(string input)
    {
        var o = OptionRegex().Matches(input).Cast<Match>().ToDictionary(x => x.Groups["key"].Value, x => x.Groups["value"].Value.Trim('"', '\''), StringComparer.OrdinalIgnoreCase);
        var hasNotes = o.ContainsKey("notes"); var hasDegrees = o.ContainsKey("degrees"); var hasGenerate = o.ContainsKey("generate"); var hasMidi = o.ContainsKey("midi_base64");
        if (new[] { hasNotes, hasDegrees, hasGenerate, hasMidi }.Count(x => x) != 1)
            throw new FormatException("Choose exactly one source: notes=..., degrees=..., generate=scale|arp|riff|melody|song|bassline|drums, or attach one MIDI file.");

        var chipExplicit = (o.ContainsKey("chip") || o.ContainsKey("preset")) && !string.Equals(Get(o, "chip", Get(o, "preset", "")), "auto", StringComparison.OrdinalIgnoreCase);
        var chip = Get(o, "chip", Get(o, "preset", "gb")).ToLowerInvariant();
        if (chip == "auto") chip = "gb";
        if (chip is "gameboy" or "dmg") chip = "gb";
        if (chip is "gameboy_color" or "color") chip = "gbc";
        if (chip == "sega") chip = "sms";
        if (chip is "c64" or "sid") chip = "c64_6581";
        if (chip is "genesis_fm" or "megadrive") chip = "genesis";
        if (chip is "pc_engine" or "turbografx") chip = "pce";
        if (chip is "atari" or "tia") chip = "atari2600";
        if (chip is "spectrum" or "zx") chip = "zx_spectrum";
        Ensure(chip, Chips, "chip");

        var instrument = Instrument(o, "instrument", "auto");
        var map = ParseInstrumentMap(o.GetValueOrDefault("instruments"));
        var leadInstrument = Instrument(o, "lead", map.GetValueOrDefault("lead", "auto"));
        var counterInstrument = Instrument(o, "counter", map.GetValueOrDefault("counter", map.GetValueOrDefault("counterlead", "auto")));
        var bassInstrument = Instrument(o, "bass", map.GetValueOrDefault("bass", "auto"));
        var harmonyInstrument = Instrument(o, "harmony", map.GetValueOrDefault("harmony", "auto"));
        var arpInstrument = Instrument(o, "arp", map.GetValueOrDefault("arp", "auto"));
        var drumsInstrument = Instrument(o, "drums", map.GetValueOrDefault("drums", "auto"));

        var style = Get(o, "style", "arcade").ToLowerInvariant(); Ensure(style, Styles, "style");
        var format = Get(o, "format", "mp3").ToLowerInvariant(); Ensure(format, Formats, "format");
        var direction = Get(o, "direction", "updown").ToLowerInvariant(); Ensure(direction, ["up", "down", "updown", "random_walk"], "direction");
        var tempoMode = Get(o, "tempo_mode", hasMidi && !o.ContainsKey("bpm") ? "file" : "override").ToLowerInvariant(); Ensure(tempoMode, ["file", "override"], "tempo_mode");
        var fidelity = Get(o, "fidelity", hasMidi ? "recognizable" : "balanced").ToLowerInvariant(); Ensure(fidelity, ["recognizable", "preserve", "balanced", "strict"], "fidelity");
        var registerMode = Get(o, "register_mode", Get(o, "register", "auto")).ToLowerInvariant(); Ensure(registerMode, ["auto", "off"], "register_mode");
        var quantize = Get(o, "quantize", hasMidi ? "off" : "1/16");
        Ensure(quantize, ["off", "1/4", "1/8", "1/16", "1/32", "1/64"], "quantize");
        var wave = Get(o, "wave", "square").ToLowerInvariant(); Ensure(wave, ["square", "triangle", "saw", "noise", "sine", "fm"], "wave");
        var sampleRate = Int(o, "sample_rate", 44_100, 44_100, 48_000);
        if (sampleRate is not (44_100 or 48_000)) throw new FormatException("sample_rate must be 44100 or 48000.");
        var generate = o.GetValueOrDefault("generate")?.ToLowerInvariant();
        if (generate is not null) Ensure(generate, GenerateModes, "generate");
        _ = MusicTheory.ParseKey(Get(o, "key", "C"));
        _ = MusicTheory.GetScale(Get(o, "scale", "major"));

        return new ChiptuneSpec
        {
            Mode = hasNotes ? ChiptuneMode.Notes : hasDegrees ? ChiptuneMode.Degrees : hasGenerate ? ChiptuneMode.Generate : ChiptuneMode.Midi,
            Notes=o.GetValueOrDefault("notes"), Degrees=o.GetValueOrDefault("degrees"), Generate=generate,
            MidiBase64=o.GetValueOrDefault("midi_base64"), Chip=chip, Instrument=instrument,
            LeadInstrument=leadInstrument, CounterInstrument=counterInstrument, BassInstrument=bassInstrument,
            HarmonyInstrument=harmonyInstrument, ArpInstrument=arpInstrument, DrumsInstrument=drumsInstrument,
            Style=style, Key=Get(o,"key","C"), Scale=Get(o,"scale","major"), Bpm=Int(o,"bpm",140,40,300), TempoMode=tempoMode, ChipExplicit=chipExplicit,
            Fidelity=fidelity, RegisterMode=registerMode, ChorusLift=Int(o,"chorus_lift",12,0,24),
            Transpose=Int(o,"transpose",0,-24,24), Octave=Int(o,"octave",4,0,8), Octaves=Int(o,"octaves",2,1,4),
            Range=o.GetValueOrDefault("range"), Direction=direction, Bars=Int(o,"bars",4,1,32), Seed=Int(o,"seed",0,int.MinValue,int.MaxValue),
            Progression=o.GetValueOrDefault("progression"),
            Quantize=quantize, Format=format, SampleRate=sampleRate, Repeat=Int(o,"repeat",1,1,8),
            Wave=wave, Duty=Int(o,"duty",25,1,99), Attack=Int(o,"attack",0,0,31), Decay=Int(o,"decay",8,0,31),
            Sustain=Int(o,"sustain",12,0,31), Release=Int(o,"release",8,0,31), Vibrato=Int(o,"vibrato",0,0,31), Filter=Int(o,"filter",0,0,2047),
            NoteCut=Int(o,"note_cut",-1,-1,255), NoteDelay=Int(o,"note_delay",0,0,255), Retrigger=Int(o,"retrigger",0,0,255),
            PitchSlide=Int(o,"pitch_slide",0,-127,127), VolumeSlide=Int(o,"volume_slide",0,-127,127)
        };
    }

    public static Song Compose(ChiptuneSpec spec)
    {
        var song = spec.Mode switch
        {
            ChiptuneMode.Notes => ParseTimedTokens(spec.Notes!, spec, false),
            ChiptuneMode.Degrees => ParseTimedTokens(spec.Degrees!, spec, true),
            ChiptuneMode.Generate => ChiptuneGenerators.Generate(spec),
            ChiptuneMode.Midi => MidiImporter.Import(Convert.FromBase64String(spec.MidiBase64!), spec),
            _ => throw new InvalidOperationException("Unsupported chiptune mode.")
        };
        if (song.Notes.Count == 0) throw new FormatException("The composition contains no valid notes.");
        song = ApplyArticulation(song, spec);
        if (spec.Repeat <= 1) return song;
        var sourceEnd = song.EndTick;
        var notes = new List<NoteEvent>(song.Notes.Count * spec.Repeat);
        for (var i = 0; i < spec.Repeat; i++)
            notes.AddRange(song.Notes.Select(n => n with { StartTick = n.StartTick + i * sourceEnd }));
        return new Song(notes, song.TempoMap, song.MidiMetadata);
    }

    private static Song ApplyArticulation(Song song, ChiptuneSpec spec)
    {
        if (spec.NoteCut < 0 && spec.NoteDelay == 0 && spec.Retrigger == 0 && spec.PitchSlide == 0 && spec.VolumeSlide == 0)
            return song;
        var notes = song.Notes.Select(note => note with
        {
            NoteCutTicks = spec.NoteCut >= 0 ? Math.Min(spec.NoteCut, Math.Max(0, (int)note.DurationTick - 1)) : note.NoteCutTicks,
            NoteDelayTicks = spec.NoteDelay,
            Retrigger = spec.Retrigger,
            PitchSlide = spec.PitchSlide,
            VolumeSlide = spec.VolumeSlide
        }).ToArray();
        return song with { Notes = notes };
    }

    private static Song ParseTimedTokens(string text, ChiptuneSpec spec, bool degrees)
    {
        var notes = new List<NoteEvent>(); long cursor = 0;
        var scale = MusicTheory.GetScale(spec.Scale); var root = MusicTheory.ParseKey(spec.Key);
        foreach (var token in text.Split([' ', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            var p = token.Split('/', 2);
            if (p.Length != 2 || !int.TryParse(p[1], out var denominator) || denominator is not (1 or 2 or 4 or 8 or 16 or 32))
                throw new FormatException($"Invalid note token '{token}'. Expected C4/8, [C4,E4]/4 or R/8.");
            var duration = 4L * TempoMap.Ppq / denominator;
            if (!p[0].Equals("R", StringComparison.OrdinalIgnoreCase))
            {
                var names = p[0].Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var name in names)
                {
                    int pitch;
                    if (degrees) pitch = MusicTheory.DegreeToPitch(name, root, scale, spec.Octave);
                    else if (!MusicTheory.TryParsePitch(name, out pitch)) throw new FormatException($"Invalid note '{name}'.");
                    pitch += spec.Transpose;
                    if (pitch is < 0 or > 127) throw new FormatException($"Note '{name}' is outside MIDI range after transposition.");
                    notes.Add(new NoteEvent(cursor, duration, pitch, 108, TrackRole.Lead, Patch: spec.Instrument));
                }
            }
            cursor += duration;
        }
        return new Song(notes, TempoMap.Fixed(spec.Bpm));
    }

    private static Dictionary<string,string> ParseInstrumentMap(string? raw)
    {
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var item in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = item.Split(':', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || pair[0].Length == 0 || pair[1].Length == 0)
                throw new FormatException("instruments must look like lead:soft_lead,bass:bass,arp:bell.");
            var role = pair[0].ToLowerInvariant();
            Ensure(role, ["lead", "counter", "counterlead", "bass", "harmony", "arp", "drums"], "instrument role");
            var value = pair[1].ToLowerInvariant(); Ensure(value, Instruments, $"instrument for {role}");
            result[role] = value;
        }
        return result;
    }

    private static string Instrument(IReadOnlyDictionary<string,string> o, string key, string fallback)
    {
        var value = Get(o, key, fallback).ToLowerInvariant();
        Ensure(value, Instruments, key == "instrument" ? "instrument" : $"{key} instrument");
        return value;
    }

    internal static IReadOnlyList<string> AvailableInstruments => Instruments;
    internal static IReadOnlyList<string> AvailableChips => Chips;
    internal static IReadOnlyList<string> AvailableStyles => Styles;
    internal static IReadOnlyList<string> AvailableGenerateModes => GenerateModes;

    private static string Get(IReadOnlyDictionary<string,string> o, string key, string fallback) => o.GetValueOrDefault(key) ?? fallback;
    private static int Int(IReadOnlyDictionary<string,string> o, string key, int fallback, int min, int max)
    {
        if (!o.TryGetValue(key, out var raw)) return fallback;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
            throw new FormatException($"{key} must be between {min} and {max}.");
        return value;
    }
    private static void Ensure(string value, IEnumerable<string> allowed, string name)
    {
        var values = allowed.ToArray();
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase)) throw new FormatException($"Unknown {name} '{value}'. Available: {string.Join(", ", values)}.");
    }
    [GeneratedRegex("(?<key>[a-zA-Z][a-zA-Z0-9_]*)=(?<value>\"[^\"]*\"|'[^']*'|[^ ]+)")] private static partial Regex OptionRegex();
}
