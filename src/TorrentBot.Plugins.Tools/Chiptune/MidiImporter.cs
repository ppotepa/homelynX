using System.Text;

namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class MidiImporter
{
    public static Song Import(byte[] bytes, ChiptuneSpec spec)
    {
        var file = Parse(bytes);
        var tempo = spec.TempoMode == "override" ? TempoMap.Fixed(spec.Bpm) : new TempoMap(file.Tempos.Select(x => new TempoPoint(ScaleTick(x.Tick, file.Division), x.Value)));
        var grid = spec.Quantize switch { "1/4" => TempoMap.Ppq, "1/8" => TempoMap.Ppq / 2, _ => TempoMap.Ppq / 4 };
        var active = new Dictionary<(int Track,int Channel,int Note), Stack<RawEvent>>();
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
                completed.Add((stack.Pop(), e.Tick));
        }
        var stats = completed.GroupBy(x => (x.Start.Track, x.Start.Channel)).Select(x => new { x.Key, Median=x.Select(n => n.Start.Note).Order().ElementAt(x.Count()/2), Count=x.Count() }).ToArray();
        var melodic = stats.Where(x => x.Key.Channel != 9).OrderByDescending(x => x.Count).ThenByDescending(x => x.Median).ToArray();
        var lead = melodic.FirstOrDefault()?.Key;
        var bass = melodic.OrderBy(x => x.Median).FirstOrDefault()?.Key;
        var notes = completed.Select(x =>
        {
            var start = Quantize(ScaleTick(x.Start.Tick, file.Division), grid);
            var end = Math.Max(start + grid, Quantize(ScaleTick(x.End, file.Division), grid));
            var role = x.Start.Channel == 9 ? TrackRole.Drums : (x.Start.Track, x.Start.Channel) == bass ? TrackRole.Bass : (x.Start.Track, x.Start.Channel) == lead ? TrackRole.Lead : TrackRole.Harmony;
            return new NoteEvent(start, end - start, Math.Clamp(x.Start.Note + spec.Transpose, 0, 127), x.Start.Value, role);
        }).OrderBy(x => x.StartTick).ThenBy(x => x.Role).ToArray();
        return new Song(notes, tempo);
    }

    private static ParsedMidi Parse(byte[] bytes)
    {
        var r = new Reader(bytes);
        if (r.Text(4) != "MThd") throw new InvalidDataException("Missing MIDI header.");
        var headerLength = r.Int32(); if (headerLength < 6) throw new InvalidDataException("Invalid MIDI header.");
        _ = r.UInt16(); var tracks = r.UInt16(); var division = r.UInt16();
        if ((division & 0x8000) != 0) throw new InvalidDataException("SMPTE MIDI timing is not supported.");
        r.Skip(headerLength - 6);
        var events = new List<RawEvent>(); var tempos = new List<RawTempo>(); var order = 0;
        for (var track = 0; track < tracks; track++)
        {
            if (r.Text(4) != "MTrk") throw new InvalidDataException("Invalid MIDI track.");
            var end = checked(r.Offset + r.Int32()); long tick = 0; var running = 0;
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
                if (type is 0x80 or 0x90) events.Add(new RawEvent(track, type, channel, first, second, tick, order++));
            }
            r.Offset = end;
        }
        return new ParsedMidi(division, events, tempos);
    }

    private static long ScaleTick(long tick, int division) => checked(tick * TempoMap.Ppq / division);
    private static long Quantize(long tick, long grid) => Math.Max(0, (long)Math.Round(tick / (double)grid, MidpointRounding.AwayFromZero) * grid);
    private sealed record ParsedMidi(int Division, IReadOnlyList<RawEvent> Events, IReadOnlyList<RawTempo> Tempos);
    private sealed record RawEvent(int Track, int Type, int Channel, int Note, int Value, long Tick, int Order);
    private sealed record RawTempo(long Tick, int Value);

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
