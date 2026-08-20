using System.Text.Json.Serialization;

namespace TorrentBot.Plugins.Tools.Chiptune;

internal enum ChiptuneMode { Notes, Degrees, Generate, Midi }
internal enum TrackRole { Lead, CounterLead, Bass, Harmony, Arp, Drums }

internal sealed record TempoPoint(long Tick, int MicrosecondsPerQuarter);

internal sealed class TempoMap
{
    public const int Ppq = 960;
    private readonly TempoPoint[] _points;

    public TempoMap(IEnumerable<TempoPoint>? points = null)
    {
        _points = new[] { new TempoPoint(0, 500_000) }.Concat(points ?? [])
            .GroupBy(x => x.Tick).Select(x => x.Last()).OrderBy(x => x.Tick).ToArray();
    }

    public double TickToSeconds(long tick)
    {
        if (tick <= 0) return 0;
        double seconds = 0;
        long cursor = 0;
        var tempo = _points[0].MicrosecondsPerQuarter;
        foreach (var point in _points.Skip(1))
        {
            if (point.Tick >= tick) break;
            seconds += (point.Tick - cursor) * tempo / 1_000_000d / Ppq;
            cursor = point.Tick;
            tempo = point.MicrosecondsPerQuarter;
        }
        return seconds + (tick - cursor) * tempo / 1_000_000d / Ppq;
    }

    public IReadOnlyList<TempoPoint> Points => _points;
    public static TempoMap Fixed(int bpm) => new([new TempoPoint(0, 60_000_000 / bpm)]);
}

internal sealed record NoteEvent(long StartTick, long DurationTick, int Pitch, int Velocity, TrackRole Role,
    int SourceTrack = -1, int SourceChannel = -1, int Program = 0, int Bank = 0,
    int Pan = 64, int Expression = 127, int PitchBend = 8192, int PitchBendRange = 2,
    IReadOnlyList<PitchBendPoint>? PitchBends = null,
    int NoteCutTicks = -1, int NoteDelayTicks = 0, int Retrigger = 0,
    int PitchSlide = 0, int VolumeSlide = 0, int Volume = 127,
    int Modulation = 0, int Aftertouch = 0, int ReleaseVelocity = 0,
    IReadOnlyList<ControllerPoint>? ControllerChanges = null,
    long KeyDurationTick = -1,
    string Section = "body", double SectionIntensity = .55, string Patch = "auto")
{
    public long EndTick => StartTick + DurationTick;
    public long KeyEndTick => StartTick + (KeyDurationTick >= 0 ? Math.Clamp(KeyDurationTick, 1, DurationTick) : DurationTick);
}

internal sealed record PitchBendPoint(long Tick, int Value);
internal sealed record ControllerPoint(long Tick, int Volume = 127, int Expression = 127, int Pan = 64, int Modulation = 0, int Aftertouch = 0);

internal sealed record Song(IReadOnlyList<NoteEvent> Notes, TempoMap TempoMap, MidiMetadata? MidiMetadata = null)
{
    public long EndTick => Notes.Count == 0 ? 0 : Notes.Max(x => x.EndTick);
    public double DurationSeconds => TempoMap.TickToSeconds(EndTick);
}

internal sealed record TimeSignaturePoint(long Tick, int Numerator, int Denominator);
internal sealed record KeySignaturePoint(long Tick, int SharpsFlats, bool Minor);
internal sealed record MidiMetadata(
    IReadOnlyDictionary<int, string> TrackNames,
    IReadOnlyList<TimeSignaturePoint> TimeSignatures,
    IReadOnlyList<KeySignaturePoint> KeySignatures,
    IReadOnlyList<MidiSourcePart>? SourceParts = null);

internal sealed record MidiSourcePart(
    int Track,
    int Channel,
    int Program,
    int Bank,
    string Name,
    TrackRole Role,
    int NoteCount,
    int PeakPolyphony);

internal sealed record ChiptuneSpec
{
    public ChiptuneMode Mode { get; init; }
    public string? Notes { get; init; }
    public string? Degrees { get; init; }
    public string? Generate { get; init; }
    [JsonIgnore] public byte[]? Midi { get; init; }
    public string? MidiBase64 { get; init; }
    public string Chip { get; init; } = "gb";
    public bool ChipExplicit { get; init; }
    public string Instrument { get; init; } = "auto";
    public string LeadInstrument { get; init; } = "auto";
    public string CounterInstrument { get; init; } = "auto";
    public string BassInstrument { get; init; } = "auto";
    public string HarmonyInstrument { get; init; } = "auto";
    public string ArpInstrument { get; init; } = "auto";
    public string DrumsInstrument { get; init; } = "auto";
    public Dictionary<string,string>? SectionInstruments { get; init; }
    public string Style { get; init; } = "arcade";
    public string Key { get; init; } = "C";
    public string Scale { get; init; } = "major";
    public int Bpm { get; init; } = 140;
    public string TempoMode { get; init; } = "file";
    public string Fidelity { get; init; } = "recognizable";
    public string RegisterMode { get; init; } = "auto";
    public int ChorusLift { get; init; } = 12;
    public int Transpose { get; init; }
    public int Octave { get; init; } = 4;
    public int Octaves { get; init; } = 2;
    public string? Range { get; init; }
    public string? Progression { get; init; }
    public string Direction { get; init; } = "updown";
    public int Bars { get; init; } = 4;
    public int Seed { get; init; }
    public string Quantize { get; init; } = "1/16";
    public string Format { get; init; } = "mp3";
    public int SampleRate { get; init; } = 44_100;
    public int Repeat { get; init; } = 1;
    public string Wave { get; init; } = "square";
    public int Duty { get; init; } = 25;
    public int Attack { get; init; }
    public int Decay { get; init; } = 8;
    public int Sustain { get; init; } = 12;
    public int Release { get; init; } = 8;
    public int Vibrato { get; init; }
    public int Filter { get; init; }
    public int NoteCut { get; init; } = -1;
    public int NoteDelay { get; init; }
    public int Retrigger { get; init; }
    public int PitchSlide { get; init; }
    public int VolumeSlide { get; init; }
}

internal sealed record HardwareNote(int Voice, long StartTick, long DurationTick, int Pitch, int Velocity, string Instrument, TrackRole Role,
    int InstrumentId = 0, int Pan = 64, int Expression = 127, int PitchBend = 8192, int PitchBendRange = 2, int Program = 0,
    int NoteCutTicks = -1, int NoteDelayTicks = 0, int Retrigger = 0, int PitchSlide = 0, int VolumeSlide = 0,
    int Volume = 127, int Modulation = 0, int Aftertouch = 0, int ReleaseVelocity = 0,
    IReadOnlyList<PitchBendPoint>? PitchBends = null, IReadOnlyList<ControllerPoint>? ControllerChanges = null,
    string VoiceClass = "pulse", string Section = "body", double SectionIntensity = .55);
internal sealed record HardwareSong(string Chip, int Bpm, int SampleRate, IReadOnlyList<TempoPoint> Tempo, IReadOnlyList<HardwareNote> Notes, long EndTick,
    string Wave = "square", int Duty = 25, int Attack = 0, int Decay = 8, int Sustain = 12, int Release = 8, int Vibrato = 0, int Filter = 0,
    int SourceNoteCount = 0, int RevoicedNotes = 0, int ArpeggiatedNotes = 0, int DroppedNotes = 0, string Fidelity = "recognizable");
