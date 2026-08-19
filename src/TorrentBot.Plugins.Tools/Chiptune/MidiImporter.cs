using System.Text;

namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class MidiImporter
{
    public static Song Import(byte[] bytes, ChiptuneSpec spec)
    {
        var file = Parse(bytes);
        var tempo = spec.TempoMode == "override" ? TempoMap.Fixed(spec.Bpm) : new TempoMap(file.Tempos.Select(x => new TempoPoint(ScaleTick(x.Tick, file.Division), x.Value)));
        var grid = spec.Quantize switch
        {
            "off" => 0,
            "1/4" => TempoMap.Ppq,
            "1/8" => TempoMap.Ppq / 2,
            "1/32" => TempoMap.Ppq / 8,
            "1/64" => TempoMap.Ppq / 16,
            _ => TempoMap.Ppq / 4
        };
        var active = new Dictionary<(int Track,int Channel,int Note), Stack<RawEvent>>();
        var sustained = new Dictionary<(int Track,int Channel), List<RawEvent>>();
        var sustain = new Dictionary<(int Track,int Channel), bool>();
        var completed = new List<(RawEvent Start, long End)>();
        foreach (var e in file.Events.OrderBy(x => x.Tick).ThenBy(x => x.Order))
        {
            var key = (e.Track, e.Channel, e.Note);
            if (e.Type == 0x90 && e.Value > 0)
            {
                if (!active.TryGetValue(key, out var stack)) active[key] = stack = new Stack<RawEvent>();
                stack.Push(e);
            }
            else if ((e.Type == 0x80 || e.Type == 0x90) && active.TryGetValue(key, out var stack) && stack.Count > 0)
            {
                var start = stack.Pop();
                var channelKey = (e.Track, e.Channel);
                if (sustain.GetValueOrDefault(channelKey))
                {
                    if (!sustained.TryGetValue(channelKey, out var held)) sustained[channelKey] = held = [];
                    held.Add(start);
                }
                else completed.Add((start, e.Tick));
            }
            else if (e.Type == 0xB0 && e.Note == 64)
            {
                var channelKey = (e.Track, e.Channel);
                var down = e.Value >= 64;
                if (!down && sustain.GetValueOrDefault(channelKey) && sustained.TryGetValue(channelKey, out var held))
                {
                    foreach (var start in held) completed.Add((start, e.Tick));
                    held.Clear();
                }
                sustain[channelKey] = down;
            }
        }
        foreach (var (channel, held) in sustained)
            foreach (var start in held)
                completed.Add((start, start.Tick + TempoMap.Ppq / 4));
        var stats = completed.GroupBy(x => (x.Start.Track, x.Start.Channel)).Select(x => new
        {
            x.Key,
            Median=x.Select(n => n.Start.Note).Order().ElementAt(x.Count()/2),
            Count=x.Count(),
            Program=x.Select(n => n.Start.Program).GroupBy(p => p).OrderByDescending(g => g.Count()).First().Key
        }).ToArray();
        var melodic = stats.Where(x => x.Key.Channel != 9).ToArray();
        var lead = melodic.OrderByDescending(x => IsLeadProgram(x.Program)).ThenByDescending(x => x.Median).ThenByDescending(x => x.Count).FirstOrDefault()?.Key;
        // A single melodic stream is a lead, never a bass. Treat the lowest
        // stream as bass only when the file actually contains another voice.
        var bass = melodic.Length > 1
            ? melodic.OrderByDescending(x => IsBassProgram(x.Program)).ThenBy(x => x.Median).ThenByDescending(x => x.Count).First().Key
            : ((int Track, int Channel)?)null;
        var notes = completed.Select(x =>
        {
            var start = Quantize(ScaleTick(x.Start.Tick, file.Division), grid);
            var end = Math.Max(start + Math.Max(1, grid), Quantize(ScaleTick(x.End, file.Division), grid));
            var role = x.Start.Channel == 9 ? TrackRole.Drums : (x.Start.Track, x.Start.Channel) == bass ? TrackRole.Bass : (x.Start.Track, x.Start.Channel) == lead ? TrackRole.Lead : TrackRole.Harmony;
            return new NoteEvent(start, end - start, Math.Clamp(x.Start.Note + spec.Transpose, 0, 127), x.Start.Value, role,
                x.Start.Track, x.Start.Channel, x.Start.Program, x.Start.Bank, x.Start.Pan, x.Start.Expression, x.Start.PitchBend);
        }).OrderBy(x => x.StartTick).ThenBy(x => x.Role).ToArray();
        return new Song(notes, tempo);
    }

    private static ParsedMidi Parse(byte[] bytes)
    {
        bytes = UnwrapRmid(bytes);
        var r = new Reader(bytes);
        if (r.Text(4) != "MThd") throw new InvalidDataException("Missing MIDI header.");
        var headerLength = r.Int32(); if (headerLength < 6) throw new InvalidDataException("Invalid MIDI header.");
        _ = r.UInt16(); var tracks = r.UInt16(); var division = r.UInt16();
        if ((division & 0x8000) != 0) throw new InvalidDataException("SMPTE MIDI timing is not supported.");
        r.Skip(headerLength - 6);
        var events = new List<RawEvent>(); var tempos = new List<RawTempo>(); var order = 0; var channelState = new MidiStateTable();
        for (var track = 0; track < tracks; track++)
        {
            if (r.Offset + 8 > bytes.Length || r.Text(4) != "MTrk") throw new InvalidDataException($"Invalid MIDI track {track + 1}/{tracks} at byte {r.Offset - 4}.");
            var trackLength = r.Int32();
            var end = checked(r.Offset + trackLength);
            if (trackLength < 0 || end > bytes.Length) throw new InvalidDataException($"MIDI track {track + 1}/{tracks} exceeds the file boundary.");
            long tick = 0; var running = 0;
            while (r.Offset < end)
            {
                tick += r.Var(); var status = r.Peek();
                if (status < 0x80) { if (running == 0) throw new InvalidDataException("Invalid MIDI running status."); status = running; }
                else { r.Skip(1); if (status < 0xF0) running = status; }
                if (status == 0xFF)
                {
                    var meta = r.Byte(); var length = checked((int)r.Var());
                    if (meta == 0x51 && length == 3) tempos.Add(new RawTempo(tick, (r.Byte() << 16) | (r.Byte() << 8) | r.Byte()));
                    else r.Skip(length);
                    continue;
                }
                if (status is 0xF0 or 0xF7) { r.Skip(checked((int)r.Var())); continue; }
                var type = status & 0xF0; var channel = status & 0x0F; var first = r.Byte(); var second = type is 0xC0 or 0xD0 ? 0 : r.Byte();
                if (type is 0x80 or 0x90)
                    events.Add(new RawEvent(track, type, channel, first, second, tick, order++, channelState[track, channel].Snapshot()));
                else if (type == 0xB0)
                {
                    channelState.Apply(track, channel, first, second);
                    events.Add(new RawEvent(track, type, channel, first, second, tick, order++, channelState[track, channel].Snapshot()));
                }
                else if (type == 0xC0)
                {
                    channelState[track, channel].Program = first;
                }
                else if (type == 0xE0)
                {
                    channelState[track, channel].PitchBend = first | (second << 7);
                }
            }
            r.Offset = end;
        }
        return new ParsedMidi(division, events, tempos);
    }

    private static byte[] UnwrapRmid(byte[] bytes)
    {
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "RMID")
            return bytes;

        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, offset, 4);
            var length = BitConverter.ToInt32(bytes, offset + 4);
            offset += 8;
            if (length < 0 || offset > bytes.Length - length) throw new InvalidDataException("Invalid RMID chunk length.");
            if (id == "data")
            {
                var midi = bytes.AsSpan(offset, length).ToArray();
                if (midi.Length < 4 || Encoding.ASCII.GetString(midi, 0, 4) != "MThd") throw new InvalidDataException("RMID data chunk does not contain a MIDI file.");
                return midi;
            }
            offset += length + (length & 1); // RIFF chunks are word aligned.
        }
        throw new InvalidDataException("RMID file has no data chunk.");
    }

    private static long ScaleTick(long tick, int division) => checked(tick * TempoMap.Ppq / division);
    private static long Quantize(long tick, long grid) => grid <= 0
        ? Math.Max(0, tick)
        : Math.Max(0, (long)Math.Round(tick / (double)grid, MidpointRounding.AwayFromZero) * grid);
    private sealed record ParsedMidi(int Division, IReadOnlyList<RawEvent> Events, IReadOnlyList<RawTempo> Tempos);
    private sealed record RawEvent(int Track, int Type, int Channel, int Note, int Value, long Tick, int Order, MidiState State)
    {
        public int Program => State.Program;
        public int Bank => State.Bank;
        public int Pan => State.Pan;
        public int Expression => State.Expression;
        public int PitchBend => State.PitchBend;
    }
    private sealed record RawTempo(long Tick, int Value);

    private sealed class MidiState
    {
        public int Program;
        public int Bank;
        public int Pan = 64;
        public int Expression = 127;
        public int PitchBend = 8192;
        public MidiState Snapshot() => new() { Program = Program, Bank = Bank, Pan = Pan, Expression = Expression, PitchBend = PitchBend };
    }

    private sealed class MidiStateTable
    {
        private readonly Dictionary<(int Track, int Channel), MidiState> _states = [];
        public MidiState this[int track, int channel] => _states.TryGetValue((track, channel), out var state) ? state : (_states[(track, channel)] = new MidiState());
        public void Apply(int track, int channel, int controller, int value)
        {
            var state = this[track, channel];
            switch (controller) { case 0: state.Bank = (value << 7) | (state.Bank & 127); break; case 32: state.Bank = (state.Bank & (127 << 7)) | value; break; case 10: state.Pan = value; break; case 11: state.Expression = value; break; }
        }
    }

    private static bool IsLeadProgram(int program) => program is >= 80 and <= 87 or >= 56 and <= 63 or >= 64 and <= 71;
    private static bool IsBassProgram(int program) => program is >= 32 and <= 39;

    private sealed class Reader(byte[] bytes)
    {
        public int Offset { get; set; }
        public int Peek() { Need(1); return bytes[Offset]; }
        public int Byte() { Need(1); return bytes[Offset++]; }
        public string Text(int length) { Need(length); var s=Encoding.ASCII.GetString(bytes,Offset,length);Offset+=length;return s; }
        public int Int32() { Need(4); var v=(bytes[Offset]<<24)|(bytes[Offset+1]<<16)|(bytes[Offset+2]<<8)|bytes[Offset+3];Offset+=4;return v; }
        public int UInt16() { Need(2); var v=(bytes[Offset]<<8)|bytes[Offset+1];Offset+=2;return v; }
        public long Var() { long value=0; for(var i=0;i<4;i++){var b=Byte();value=(value<<7)|(uint)(b&0x7f);if((b&0x80)==0)return value;}throw new InvalidDataException("Invalid MIDI variable-length value."); }
        public void Skip(int count) { if(count<0)throw new InvalidDataException("Invalid MIDI length.");Need(count);Offset+=count; }
        private void Need(int count) { if(Offset<0||count<0||Offset>bytes.Length-count)throw new InvalidDataException("Unexpected end of MIDI file."); }
    }
}
