using System.Text;

namespace TorrentBot.Plugins.Tools.Chiptune;

// Deterministic register-level styled renderer used by tests and local development.
// Production prefers the Furnace helper when CHIPTUNE_RENDERER_PATH is configured.
internal static class ManagedChipRenderer
{
    public static byte[] Render(HardwareSong song)
    {
        var tempo = new TempoMap(song.Tempo);
        var seconds = Math.Max(.1, tempo.TickToSeconds(song.EndTick));
        var frames = checked((int)Math.Ceiling(seconds * song.SampleRate));
        var left = new float[frames]; var right = new float[frames];
        foreach (var note in song.Notes)
        {
            var start = Math.Clamp((int)(tempo.TickToSeconds(note.StartTick) * song.SampleRate), 0, frames);
            var end = Math.Clamp((int)(tempo.TickToSeconds(note.StartTick + note.DurationTick) * song.SampleRate), start, frames);
            RenderVoice(song, note, left, right, start, end, song.SampleRate);
        }
        return Wav(left, right, song.SampleRate);
    }

    private static void RenderVoice(HardwareSong song, HardwareNote note, float[] left, float[] right, int start, int end, int rate)
    {
        var chip = song.Chip;
        var frequency = 440d * Math.Pow(2, (note.Pitch - 69) / 12d);
        uint lfsr = (uint)(0x7fff ^ note.Pitch ^ note.Voice * 131); double phase = 0;
        var pan = chip == "snes" ? (note.Voice % 3 - 1) * .35 : note.Voice switch { 0 => -.18, 1 => .18, _ => 0 };
        for (var i = start; i < end; i++)
        {
            var age = (i - start) / (double)rate; var remain = (end - i) / (double)rate;
            var attack = Math.Min(1, age / (note.Instrument == "soft_lead" ? .03 : .006));
            var release = Math.Min(1, remain / (note.Instrument == "bell" ? .18 : .025));
            var decay = note.Instrument switch { "pluck" => Math.Exp(-age * 7), "bell" => Math.Exp(-age * 3), _ => .78 + .22 * Math.Exp(-age * 5) };
            var vibrato = note.Instrument is "lead" or "soft_lead" ? Math.Sin(age * Math.PI * 11) * .0025 : 0;
            phase += frequency * (1 + vibrato) / rate;
            var sample = Wave(song, note, phase, ref lfsr, i);
            var gain = (float)(sample * attack * release * decay * note.Velocity / 127d * .16);
            left[i] += gain * (float)(1 - pan); right[i] += gain * (float)(1 + pan);
        }
    }

    private static double Wave(HardwareSong song, HardwareNote note, double phase, ref uint lfsr, int sample)
    {
        var chip = song.Chip;
        if (note.Role == TrackRole.Drums || note.Instrument == "drums")
        {
            var bit = ((lfsr >> 0) ^ (lfsr >> (chip == "gameboy" ? 1 : 6))) & 1;
            lfsr = (lfsr >> 1) | (bit << 14);
            return (lfsr & 1) == 0 ? -.8 : .8;
        }
        var p = phase - Math.Floor(phase);
        if (song.Wave is "triangle" or "saw" or "sine")
            return song.Wave switch { "triangle" => 4 * Math.Abs(p - .5) - 1, "saw" => 2 * p - 1, _ => Math.Sin(p * Math.PI * 2) };
        if (song.Wave == "noise")
        {
            lfsr = (lfsr >> 1) | ((((lfsr >> 0) ^ (lfsr >> 1)) & 1) << 14);
            return (lfsr & 1) == 0 ? -.8 : .8;
        }
        return chip switch
        {
            "nes" when note.Voice == 2 => 4 * Math.Abs(p - .5) - 1,
            "gameboy" when note.Voice == 2 => Wavetable(p, note.Instrument),
            "snes" => SampleBank(p, note.Instrument),
            "sms" => p < (note.Voice == 1 ? .5 : .25) ? 1 : -1,
            _ => p < song.Duty / 100d ? 1 : -1
        };
    }
    private static double Wavetable(double p, string instrument) => instrument == "bass" ? Math.Sin(p * Math.PI * 2) * .75 + (p < .5 ? .25 : -.25) : Math.Sin(p * Math.PI * 2);
    private static double SampleBank(double p, string instrument) => instrument switch
    { "bell" => Math.Sin(p*Math.PI*2)+.35*Math.Sin(p*Math.PI*6), "pluck" => 1-2*p, "bass" => Math.Sin(p*Math.PI*2), _ => p<.5?1:-1 };

    private static byte[] Wav(float[] left, float[] right, int rate)
    {
        var peak = Math.Max(.001f, Math.Max(left.Max(Math.Abs), right.Max(Math.Abs))); var scale = peak > .95f ? .95f / peak : 1;
        using var output = new MemoryStream(); using var w = new BinaryWriter(output, Encoding.ASCII, true);
        var dataSize = left.Length * 4;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataSize); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataSize);
        for(var i=0;i<left.Length;i++){w.Write((short)Math.Clamp(left[i]*scale*short.MaxValue,short.MinValue,short.MaxValue));w.Write((short)Math.Clamp(right[i]*scale*short.MaxValue,short.MinValue,short.MaxValue));}
        return output.ToArray();
    }
}
