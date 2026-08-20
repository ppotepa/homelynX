using System.Text;

namespace TorrentBot.Plugins.Tools.Chiptune;

// Deterministic chip-styled renderer used by tests and local development.
// Production prefers the Furnace helper when CHIPTUNE_RENDERER_PATH is configured,
// but this renderer intentionally preserves the same performance data and patches.
internal static class ManagedChipRenderer
{
    public static byte[] Render(HardwareSong song)
    {
        var tempo = new TempoMap(song.Tempo);
        var seconds = Math.Max(.1, tempo.TickToSeconds(song.EndTick));
        var frames = checked((int)Math.Ceiling(seconds * song.SampleRate));
        var left = new float[frames];
        var right = new float[frames];
        foreach (var note in song.Notes)
        {
            var start = Math.Clamp((int)Math.Round(tempo.TickToSeconds(note.StartTick) * song.SampleRate), 0, frames);
            var end = Math.Clamp((int)Math.Round(tempo.TickToSeconds(note.StartTick + note.DurationTick) * song.SampleRate), start, frames);
            if (end > start) RenderVoice(song, note, tempo, left, right, start, end, song.SampleRate);
        }
        return Wav(left, right, song.SampleRate);
    }

    private static void RenderVoice(HardwareSong song, HardwareNote note, TempoMap tempo, float[] left, float[] right, int start, int end, int rate)
    {
        var bendPoints = (note.PitchBends ?? [])
            .Select(point => (Sample: Math.Clamp((int)Math.Round(tempo.TickToSeconds(point.Tick) * rate), start, end), point.Value))
            .OrderBy(x => x.Sample)
            .ToArray();
        var controllerPoints = (note.ControllerChanges ?? [])
            .Select(point => (Sample: Math.Clamp((int)Math.Round(tempo.TickToSeconds(point.Tick) * rate), start, end), Point: point))
            .OrderBy(x => x.Sample)
            .ToArray();

        var bendIndex = 0;
        var controllerIndex = 0;
        var currentBend = note.PitchBend;
        var currentVolume = note.Volume;
        var currentExpression = note.Expression;
        var currentPan = note.Pan;
        var currentModulation = Math.Max(note.Modulation, note.Aftertouch);
        var initialBendSemitones = BendSemitones(note.PitchBend, note.PitchBendRange);
        var unbentPitch = note.Pitch - Math.Round(initialBendSemitones);
        uint lfsr = (uint)(0x7fff ^ note.Pitch ^ note.Voice * 131 ^ note.InstrumentId * 977);
        double phase = 0;

        for (var i = start; i < end; i++)
        {
            while (bendIndex < bendPoints.Length && bendPoints[bendIndex].Sample <= i)
                currentBend = bendPoints[bendIndex++].Value;
            while (controllerIndex < controllerPoints.Length && controllerPoints[controllerIndex].Sample <= i)
            {
                var point = controllerPoints[controllerIndex++].Point;
                currentVolume = point.Volume;
                currentExpression = point.Expression;
                currentPan = point.Pan;
                currentModulation = Math.Max(point.Modulation, point.Aftertouch);
            }

            var age = (i - start) / (double)rate;
            var remain = (end - i) / (double)rate;
            var bend = BendSemitones(currentBend, note.PitchBendRange);
            var pitch = unbentPitch + bend;
            var frequency = 440d * Math.Pow(2, (pitch - 69) / 12d);
            var vibratoDepth = currentModulation / 127d * .0125;
            var vibrato = currentModulation == 0 ? 0 : Math.Sin(age * Math.PI * 10) * vibratoDepth;
            var percussion = note.Role == TrackRole.Drums || IsPercussion(note.Instrument);
            if (percussion)
                frequency = note.Instrument switch { "kick" => 72 - Math.Min(35, age * 160), "tom" => 120 - Math.Min(45, age * 120), _ => frequency };
            phase += frequency * (1 + vibrato) / rate;

            var envelope = Envelope(note.Instrument, age, remain, percussion);
            var sample = Wave(song, note, phase, age, ref lfsr);
            var gain = sample * envelope * note.Velocity / 127d * currentVolume / 127d * currentExpression / 127d * .15;

            var normalizedPan = Math.Clamp((currentPan - 64) / 63d, -1, 1);
            var angle = (normalizedPan + 1) * Math.PI / 4;
            var leftGain = Math.Cos(angle);
            var rightGain = Math.Sin(angle);
            left[i] += (float)(gain * leftGain);
            right[i] += (float)(gain * rightGain);
        }
    }

    private static double BendSemitones(int value, int range)
        => (value - 8192) / 8192d * Math.Clamp(range, 0, 24);

    private static double Envelope(string instrument, double age, double remain, bool percussion)
    {
        if (percussion)
        {
            var percussionDecay = instrument switch
            {
                "kick" => 12d,
                "snare" => 18d,
                "hat" => 35d,
                "open_hat" => 9d,
                "tom" => 14d,
                "crash" => 5d,
                "ride" => 4d,
                _ => 12d
            };
            return Math.Exp(-age * percussionDecay) * Math.Min(1, remain / .006);
        }

        var attackTime = instrument switch
        {
            "pad" => .12,
            "strings" => .08,
            "soft_lead" => .025,
            "flute" => .018,
            "reed" => .014,
            "brass" => .018,
            _ => .004
        };
        var releaseTime = instrument switch
        {
            "pad" or "strings" => .16,
            "flute" => .08,
            "bell" => .12,
            "epiano" => .08,
            _ => .025
        };
        var attack = Math.Min(1, age / attackTime);
        var release = Math.Min(1, remain / releaseTime);
        var decay = instrument switch
        {
            "pluck" => .25 + .75 * Math.Exp(-age * 8),
            "bell" => .28 + .72 * Math.Exp(-age * 3.2),
            "epiano" => .45 + .55 * Math.Exp(-age * 2.5),
            "bass" => .72 + .28 * Math.Exp(-age * 5),
            "pad" => .92,
            "strings" => .88,
            "flute" => .9,
            "reed" => .84,
            _ => .80 + .20 * Math.Exp(-age * 5)
        };
        return attack * release * decay;
    }

    private static double Wave(HardwareSong song, HardwareNote note, double phase, double age, ref uint lfsr)
    {
        if (note.Role == TrackRole.Drums || IsPercussion(note.Instrument))
            return Percussion(note.Instrument, phase, age, song.Chip, ref lfsr);

        var p = phase - Math.Floor(phase);
        if (song.Wave is "triangle" or "saw" or "sine" && note.VoiceClass is not "fm" and not "sample")
            return song.Wave switch { "triangle" => Triangle(p), "saw" => Saw(p), _ => Math.Sin(p * Math.PI * 2) };
        if (song.Wave == "noise") return Noise(song.Chip, ref lfsr);

        return note.VoiceClass switch
        {
            "triangle" => Triangle(p),
            "wave" or "wavetable" => Wavetable(p, note.Instrument),
            "sample" => SampleBank(p, note.Instrument),
            "fm" => Fm(p, note.Instrument),
            "pokey" => Pokey(p, note.Instrument, ref lfsr),
            "tia" => Tia(p, note.Instrument, ref lfsr),
            "beeper" => p < .5 ? 1 : -1,
            "noise" => Noise(song.Chip, ref lfsr),
            _ => ChipPulse(song, note, p)
        };
    }

    private static double ChipPulse(HardwareSong song, HardwareNote note, double p)
    {
        if (song.Chip is "c64_6581" or "c64_8580")
        {
            if (note.Instrument is "bass" or "brass" or "organ" or "pad") return .6 * Saw(p) + .4 * Pulse(p, .5);
            if (note.Instrument is "bell" or "strings" or "reed" or "flute") return .65 * Triangle(p) + .35 * Math.Sin(p * Math.PI * 2);
        }
        var duty = note.Instrument switch
        {
            "soft_lead" or "strings" or "pad" or "organ" => .5,
            "brass" => .75,
            "pluck" or "epiano" => .25,
            "reed" => .375,
            "flute" => .5,
            _ => song.Duty / 100d
        };
        if (song.Chip == "sms" && note.Voice == 1) duty = .5;
        return Pulse(p, Math.Clamp(duty, .125, .875));
    }

    private static double Fm(double p, string instrument)
    {
        var (ratio, index, carrier2) = instrument switch
        {
            "bell" => (4.0, 2.8, .35),
            "epiano" => (3.0, 1.8, .25),
            "bass" => (2.0, 2.2, .08),
            "brass" => (1.0, 2.5, .18),
            "organ" => (2.0, .45, .45),
            "strings" or "pad" => (1.0, .8, .30),
            "pluck" => (3.0, 2.0, .15),
            "soft_lead" => (2.0, .9, .18),
            "reed" => (2.0, 1.2, .22),
            "flute" => (1.0, .38, .10),
            _ => (2.0, 2.6, .12)
        };
        var mod = Math.Sin(p * Math.PI * 2 * ratio) * index;
        return Math.Sin(p * Math.PI * 2 + mod) * (1 - carrier2) + Math.Sin(p * Math.PI * 4) * carrier2;
    }

    private static double Pokey(double p, string instrument, ref uint lfsr)
    {
        if (instrument == "bass") return .7 * Pulse(p, .5) + .3 * Pulse((p * 2) % 1, .5);
        if (instrument == "reed") return .72 * Pulse(p, .5) + .28 * Saw(p);
        if (instrument == "flute") return .78 * Pulse(p, .5) + .22 * Triangle(p);
        if (instrument == "bell") return .65 * Pulse(p, .25) + .35 * Pulse((p * 3) % 1, .5);
        if (IsPercussion(instrument)) return Noise("pokey", ref lfsr);
        return Pulse(p, .5);
    }

    private static double Tia(double p, string instrument, ref uint lfsr)
    {
        if (IsPercussion(instrument) && instrument is not "kick" and not "tom") return Noise("atari2600", ref lfsr);
        if (instrument is "bass" or "kick" or "tom") return Pulse(p, .5);
        if (instrument == "flute") return .75 * Pulse(p, .5) + .25 * Pulse((p * 2) % 1, .5);
        if (instrument is "reed" or "bell") return .65 * Pulse(p, .33) + .35 * Pulse((p * 2) % 1, .5);
        return Pulse(p, .5);
    }

    private static double Percussion(string instrument, double phase, double age, string chip, ref uint lfsr)
    {
        var p = phase - Math.Floor(phase);
        return instrument switch
        {
            "kick" => .9 * Math.Sin(p * Math.PI * 2) + .1 * Noise(chip, ref lfsr),
            "tom" => .8 * Math.Sin(p * Math.PI * 2) + .2 * Noise(chip, ref lfsr),
            "snare" => .72 * Noise(chip, ref lfsr) + .28 * Math.Sin(p * Math.PI * 2),
            "ride" => .7 * Noise(chip, ref lfsr) + .3 * Math.Sin(p * Math.PI * 12),
            "crash" => .85 * Noise(chip, ref lfsr) + .15 * Math.Sin(p * Math.PI * 18),
            _ => Noise(chip, ref lfsr)
        };
    }

    private static double Noise(string chip, ref uint lfsr)
    {
        var tap = chip switch
        {
            "gb" or "gbc" or "gameboy" => 1,
            "nes" => 6,
            "pokey" => 5,
            _ => 1
        };
        var bit = ((lfsr >> 0) ^ (lfsr >> tap)) & 1;
        lfsr = (lfsr >> 1) | (bit << 14);
        return (lfsr & 1) == 0 ? -.82 : .82;
    }

    private static bool IsPercussion(string instrument) => instrument is
        "drums" or "kick" or "snare" or "hat" or "open_hat" or "tom" or "crash" or "ride";

    private static double Pulse(double p, double duty) => p < duty ? 1 : -1;
    private static double Triangle(double p) => 4 * Math.Abs(p - .5) - 1;
    private static double Saw(double p) => 2 * p - 1;

    private static double Wavetable(double p, string instrument) => instrument switch
    {
        "lead" => .72 * Pulse(p, .5) + .28 * Math.Sin(p * Math.PI * 2),
        "soft_lead" => .90 * Math.Sin(p * Math.PI * 2) + .07 * Math.Sin(p * Math.PI * 4),
        "pluck" => .68 * Saw(p) + .32 * Math.Sin(p * Math.PI * 2),
        "bass" => .58 * Saw(p) + .42 * Math.Sin(p * Math.PI * 2),
        "bell" => .62 * Math.Sin(p * Math.PI * 2) + .25 * Math.Sin(p * Math.PI * 6) + .13 * Math.Sin(p * Math.PI * 10),
        "strings" or "pad" => .72 * Math.Sin(p * Math.PI * 2) + .2 * Math.Sin(p * Math.PI * 4) + .08 * Math.Sin(p * Math.PI * 6),
        "brass" => .55 * Saw(p) + .45 * Math.Sin(p * Math.PI * 2),
        "organ" => .58 * Math.Sin(p * Math.PI * 2) + .25 * Math.Sin(p * Math.PI * 4) + .13 * Math.Sin(p * Math.PI * 8),
        "epiano" => .72 * Math.Sin(p * Math.PI * 2) + .20 * Math.Sin(p * Math.PI * 8),
        "reed" => .82 * Math.Sin(p * Math.PI * 2) + .16 * Math.Sin(p * Math.PI * 4),
        "flute" => .94 * Math.Sin(p * Math.PI * 2) + .05 * Math.Sin(p * Math.PI * 4),
        _ => Math.Sin(p * Math.PI * 2)
    };

    private static double SampleBank(double p, string instrument) => instrument switch
    {
        "lead" => .70 * Pulse(p, .5) + .28 * Math.Sin(p*Math.PI*2),
        "soft_lead" => .90 * Math.Sin(p*Math.PI*2) + .07 * Math.Sin(p*Math.PI*4),
        "bell" => .55*Math.Sin(p*Math.PI*2)+.25*Math.Sin(p*Math.PI*6)+.12*Math.Sin(p*Math.PI*10),
        "pluck" => .7*Saw(p)+.3*Math.Sin(p*Math.PI*2),
        "bass" => .55*Saw(p)+.45*Math.Sin(p*Math.PI*2),
        "strings" or "pad" => .68*Math.Sin(p*Math.PI*2)+.2*Math.Sin(p*Math.PI*4)+.1*Math.Sin(p*Math.PI*6),
        "brass" => .5*Saw(p)+.5*Math.Sin(p*Math.PI*2),
        "organ" => .55*Math.Sin(p*Math.PI*2)+.28*Math.Sin(p*Math.PI*4)+.14*Math.Sin(p*Math.PI*8),
        "epiano" => .72*Math.Sin(p*Math.PI*2)+.2*Math.Sin(p*Math.PI*8),
        "reed" => .84*Math.Sin(p*Math.PI*2)+.14*Math.Sin(p*Math.PI*4),
        "flute" => .96*Math.Sin(p*Math.PI*2)+.035*Math.Sin(p*Math.PI*4),
        _ => p<.5?1:-1
    };

    private static byte[] Wav(float[] left, float[] right, int rate)
    {
        var peak = Math.Max(.001f, Math.Max(left.Max(Math.Abs), right.Max(Math.Abs)));
        var scale = peak > .95f ? .95f / peak : 1;
        using var output = new MemoryStream();
        using var w = new BinaryWriter(output, Encoding.ASCII, true);
        var dataSize = left.Length * 4;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataSize); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataSize);
        for(var i=0;i<left.Length;i++)
        {
            w.Write((short)Math.Clamp(left[i]*scale*short.MaxValue, short.MinValue, short.MaxValue));
            w.Write((short)Math.Clamp(right[i]*scale*short.MaxValue, short.MinValue, short.MaxValue));
        }
        return output.ToArray();
    }
}
