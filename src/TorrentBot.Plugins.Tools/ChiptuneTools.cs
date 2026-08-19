using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Tools;

internal static class ChiptuneTools
{
    public static async Task<CapabilityResult> ExecuteAsync(string input, CancellationToken ct)
    {
        var options = ParseOptions(input);
        var preset = options.GetValueOrDefault("preset", "gameboy").ToLowerInvariant();
        var format = options.GetValueOrDefault("format", "wav").ToLowerInvariant();
        var bpm = ParseInt(options.GetValueOrDefault("bpm"), 140, 40, 300);
        var transpose = ParseInt(options.GetValueOrDefault("transpose"), 0, -24, 24);
        var notes = options.TryGetValue("notes", out var noteText)
            ? ParseNotes(noteText, bpm, transpose)
            : options.TryGetValue("midi_base64", out var encoded)
                ? ParseMidi(Convert.FromBase64String(encoded), transpose)
                : [];

        if (notes.Count == 0) return new(true, null, "Usage: /chiptune notes=\"C4/8 E4/8 G4/4\" bpm=140 preset=gameboy format=mp3");
        var duration = Math.Clamp(notes.Max(n => n.Start + n.Duration), 0.1, 600);
        var wav = Render(notes, duration, preset);
        var output = await EncodeAsync(wav, format, ct);
        var extension = format is "mp3" or "ogg" or "wav" ? format : "wav";
        var type = extension switch { "mp3" => "audio/mpeg", "ogg" => "audio/ogg", _ => "audio/wav" };
        return FeatureArtifacts.Binary($"chiptune.{extension}", type, output, $"Chiptune generated: {preset}, {duration:F1}s, {notes.Count} notes.");
    }

    private static List<ChipNote> ParseNotes(string text, int bpm, int transpose)
    {
        var result = new List<ChipNote>();
        var cursor = 0.0;
        var beat = 60.0 / bpm;
        foreach (var token in text.Split([' ', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = token.Split('/', 2);
            if (pieces.Length != 2 || !double.TryParse(pieces[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) || denominator <= 0) continue;
            var duration = beat * 4 / denominator;
            if (pieces[0].Equals("R", StringComparison.OrdinalIgnoreCase)) { cursor += duration; continue; }
            foreach (var name in pieces[0].Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (TryNoteNumber(name, out var number)) result.Add(new ChipNote(cursor, duration, number + transpose, 0.85, result.Count % 4));
            cursor += duration;
        }
        return result;
    }

    private static List<ChipNote> ParseMidi(byte[] bytes, int transpose)
    {
        var reader = new MidiReader(bytes);
        var result = new List<ChipNote>();
        var active = new Dictionary<(int Channel, int Note), (double Start, double Velocity)>();
        foreach (var evt in reader.Read())
        {
            if (evt.Type == 0x90 && evt.Value > 0) active[(evt.Channel, evt.Note)] = (evt.Time, evt.Value / 127.0);
            else if ((evt.Type == 0x80 || evt.Type == 0x90) && active.Remove((evt.Channel, evt.Note), out var start))
                result.Add(new ChipNote(start.Start, Math.Max(0.02, evt.Time - start.Start), evt.Note + transpose, start.Velocity, evt.Channel == 9 ? 9 : evt.Channel % 4));
        }
        return result;
    }

    private static byte[] Render(List<ChipNote> notes, double seconds, string preset)
    {
        const int sampleRate = 22050;
        var pcm = new short[checked((int)Math.Ceiling(seconds * sampleRate))];
        foreach (var note in notes)
        {
            var start = Math.Clamp((int)(note.Start * sampleRate), 0, pcm.Length);
            var end = Math.Clamp((int)((note.Start + note.Duration) * sampleRate), start, pcm.Length);
            var frequency = 440 * Math.Pow(2, (note.MidiNote - 69) / 12.0);
            for (var i = start; i < end; i++)
            {
                var t = (i - start) / (double)sampleRate;
                var phase = t * frequency;
                var envelope = Math.Min(1, t * 40) * Math.Min(1, Math.Max(0, note.Duration - t) * 20);
                var wave = note.Channel == 9 ? Noise(i, note.MidiNote) : preset == "nes" && note.Channel == 2 ? Triangle(phase) : Square(phase, preset == "c64" ? 0.5 : 0.25);
                pcm[i] = (short)Math.Clamp(pcm[i] + (int)(wave * envelope * note.Velocity * 4500), short.MinValue, short.MaxValue);
            }
        }

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length * 2);
        foreach (var sample in pcm) writer.Write(sample);
        return output.ToArray();
    }

    private static async Task<byte[]> EncodeAsync(byte[] wav, string format, CancellationToken ct)
    {
        if (format == "wav") return wav;
        var codec = format == "ogg" ? "libvorbis" : "libmp3lame";
        var muxer = format == "ogg" ? "ogg" : "mp3";
        var start = new ProcessStartInfo("ffmpeg") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-f", "wav", "-i", "pipe:0", "-c:a", codec, "-b:a", "192k", "-f", muxer, "pipe:1" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg is not installed.");
        await process.StandardInput.BaseStream.WriteAsync(wav, ct); process.StandardInput.Close();
        using var output = new MemoryStream();
        var read = process.StandardOutput.BaseStream.CopyToAsync(output, ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(read, process.WaitForExitAsync(ct), error);
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Result.Trim());
        return output.ToArray();
    }

    private static bool TryNoteNumber(string text, out int value)
    {
        value = 0;
        var match = Regex.Match(text.Trim(), "^(?<note>[A-Ga-g])(?<acc>[#b]?)(?<oct>-?\\d+)$");
        if (!match.Success || !int.TryParse(match.Groups["oct"].Value, out var octave)) return false;
        var semitone = match.Groups["note"].Value.ToUpperInvariant()[0] switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => 0 };
        if (match.Groups["acc"].Value == "#") semitone++;
        if (match.Groups["acc"].Value == "b") semitone--;
        value = (octave + 1) * 12 + semitone;
        return value is >= 0 and <= 127;
    }

    private static double Square(double phase, double duty) => phase % 1 < duty ? 1 : -1;
    private static double Triangle(double phase) => 2 * Math.Abs(2 * (phase % 1) - 1) - 1;
    private static double Noise(int sample, int note) => (unchecked(sample * 1103515245 + note * 12345) & 1) == 0 ? 1 : -1;
    private static int ParseInt(string? value, int fallback, int min, int max) => int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
    private static Dictionary<string, string> ParseOptions(string input) => Regex.Matches(input, "(?<key>[a-zA-Z][a-zA-Z0-9_]*)=(?<value>\\\"[^\\\"]*\\\"|'[^']*'|[^ ]+)").Cast<Match>().ToDictionary(x => x.Groups["key"].Value, x => x.Groups["value"].Value.Trim('"', '\''), StringComparer.OrdinalIgnoreCase);
    private sealed record ChipNote(double Start, double Duration, int MidiNote, double Velocity, int Channel);

    private sealed class MidiReader
    {
        private readonly byte[] _bytes;
        private int _offset;
        private readonly int _division;
        private readonly int _tracks;
        private double _tempo = 500000;

        public MidiReader(byte[] bytes)
        {
            _bytes = bytes;
            if (ReadText() != "MThd") throw new InvalidDataException("Missing MIDI header.");
            var length = ReadInt32();
            if (length < 6) throw new InvalidDataException("Invalid MIDI header.");
            _ = ReadUInt16(); _tracks = ReadUInt16(); _division = ReadUInt16(); _offset += length - 6;
            if (_division <= 0) throw new InvalidDataException("SMPTE MIDI timing is not supported.");
        }

        public IEnumerable<MidiEvent> Read()
        {
            for (var track = 0; track < _tracks; track++)
            {
                if (ReadText() != "MTrk") throw new InvalidDataException("Invalid MIDI track.");
                var length = ReadInt32(); var end = _offset + length; long ticks = 0; var running = 0;
                while (_offset < end)
                {
                    ticks += ReadVar(); var status = (int)_bytes[_offset]; if (status < 0x80) status = running; else _offset++;
                    if (status < 0xF0) running = status;
                    if (status == 0xFF)
                    {
                        var meta = _bytes[_offset++]; var size = (int)ReadVar();
                        if (meta == 0x51 && size == 3) _tempo = (_bytes[_offset] << 16) | (_bytes[_offset + 1] << 8) | _bytes[_offset + 2];
                        _offset += size; continue;
                    }
                    if (status is 0xF0 or 0xF7) { _offset += (int)ReadVar(); continue; }
                    var type = status & 0xF0; var channel = status & 0x0F; var note = _bytes[_offset++]; var data = type is 0xC0 or 0xD0 ? 0 : _bytes[_offset++];
                    yield return new MidiEvent(type, channel, note, data, ticks * _tempo / 1_000_000.0 / _division);
                }
                _offset = end;
            }
        }

        private string ReadText() { var text = Encoding.ASCII.GetString(_bytes, _offset, 4); _offset += 4; return text; }
        private int ReadInt32() { var value = (_bytes[_offset] << 24) | (_bytes[_offset + 1] << 16) | (_bytes[_offset + 2] << 8) | _bytes[_offset + 3]; _offset += 4; return value; }
        private int ReadUInt16() { var value = (_bytes[_offset] << 8) | _bytes[_offset + 1]; _offset += 2; return value; }
        private uint ReadVar() { uint value = 0; byte current; do { current = _bytes[_offset++]; value = (value << 7) | (uint)(current & 0x7F); } while ((current & 0x80) != 0); return value; }
    }

    private sealed record MidiEvent(int Type, int Channel, int Note, int Value, double Time);
}
