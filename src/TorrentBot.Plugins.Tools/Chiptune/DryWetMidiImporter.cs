using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;

namespace TorrentBot.Plugins.Tools.Chiptune;

/// <summary>
/// Converts a Standard MIDI/RMID file into the application performance model.
/// DryWetMIDI owns SMF decoding; arranger policy remains in this project.
/// </summary>
internal static class DryWetMidiImporter
{
    public static Song Import(byte[] bytes, ChiptuneSpec spec)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var file = MidiFile.Read(stream);
        if (file.TimeDivision is not TicksPerQuarterNoteTimeDivision division)
            throw new InvalidDataException("SMPTE MIDI timing is not supported.");

        var ppq = division.TicksPerQuarterNote;
        var tracks = file.GetTrackChunks().ToArray();
        var timedByTrack = tracks.Select(track => track.GetTimedEvents().ToArray()).ToArray();
        var tempoEvents = timedByTrack.SelectMany(events => events)
            .Where(x => x.Event is SetTempoEvent)
            .Select(x => new TempoPoint(Scale(x.Time, ppq), checked((int)((SetTempoEvent)x.Event).MicrosecondsPerQuarterNote)))
            .ToArray();
        var tempo = spec.TempoMode == "override"
            ? TempoMap.Fixed(spec.Bpm)
            : new TempoMap(tempoEvents);

        var names = new Dictionary<int, string>();
        var signatures = new List<TimeSignaturePoint>();
        var keys = new List<KeySignaturePoint>();
        foreach (var events in timedByTrack)
        {
            foreach (var timed in events)
            {
                switch (timed.Event)
                {
                    case SequenceTrackNameEvent name when !string.IsNullOrWhiteSpace(name.Text):
                        names.TryAdd(Array.IndexOf(timedByTrack, events), name.Text.Trim());
                        break;
                    case TimeSignatureEvent signature:
                        signatures.Add(new TimeSignaturePoint(Scale(timed.Time, ppq), signature.Numerator, signature.Denominator));
                        break;
                    case KeySignatureEvent key:
                        keys.Add(new KeySignaturePoint(Scale(timed.Time, ppq), key.Key, key.Scale != 0));
                        break;
                }
            }
        }

        var completed = new List<ImportedNote>();
        for (var trackIndex = 0; trackIndex < tracks.Length; trackIndex++)
        {
            var events = timedByTrack[trackIndex];
            var notes = tracks[trackIndex].GetNotes().OrderBy(x => x.Time).ToArray();
            var state = new StateTable(events);
            foreach (var note in notes)
            {
                state.AdvanceTo(note.Time);
                var end = note.Time + note.Length;
                var sustainedEnd = state.SustainExtension(note.Channel, end);
                var bends = state.Bends(note.Channel, note.Time, end);
                var automation = state.Automation(note.Channel, note.Time, end);
                completed.Add(new ImportedNote(trackIndex, note, state.Snapshot(note.Channel), sustainedEnd, bends, automation));
            }
        }

        if (completed.Count == 0) throw new InvalidDataException("MIDI file contains no notes.");
        var stats = completed.GroupBy(x => (x.Track, Channel: (int)x.Note.Channel)).Select(group => new
        {
            group.Key,
            Median = group.Select(x => (int)x.Note.NoteNumber).Order().ElementAt(group.Count() / 2),
            Count = group.Count(),
            Program = group.Select(x => x.State.Program).GroupBy(x => x).OrderByDescending(x => x.Count()).First().Key
        }).ToArray();
        var melodic = stats.Where(x => x.Key.Channel != 9).ToArray();
        var lead = melodic.OrderByDescending(x => IsLeadProgram(x.Program)).ThenByDescending(x => x.Median).ThenByDescending(x => x.Count).FirstOrDefault()?.Key;
        var bass = melodic.Length > 1
            ? melodic.OrderByDescending(x => IsBassProgram(x.Program)).ThenBy(x => x.Median).ThenByDescending(x => x.Count).First().Key
            : ((int Track, int Channel)?)null;
        var grid = spec.Quantize switch
        {
            "off" => 0L,
            "1/4" => TempoMap.Ppq,
            "1/8" => TempoMap.Ppq / 2,
            "1/32" => TempoMap.Ppq / 8,
            "1/64" => TempoMap.Ppq / 16,
            _ => TempoMap.Ppq / 4
        };

        var result = completed.SelectMany(item =>
        {
            var rawStart = Scale(item.Note.Time, ppq);
            var rawEnd = Scale(item.End, ppq);
            var start = Quantize(rawStart, grid);
            var end = Math.Max(start + Math.Max(1, grid), Quantize(rawEnd, grid));
            var key = (item.Track, Channel: (int)item.Note.Channel);
            var role = item.Note.Channel == 9 ? TrackRole.Drums : key == bass ? TrackRole.Bass : key == lead ? TrackRole.Lead : TrackRole.Harmony;
            var boundaries = new[] { item.Note.Time }
                .Concat(item.Bends.Select(x => x.Time))
                .Concat(item.Automation.Select(x => x.Time))
                .Append(item.End).Distinct().Order().ToArray();
            var currentBend = item.State.PitchBend;
            return boundaries.Zip(boundaries.Skip(1), (rawStart, rawEnd) =>
            {
                var segmentState = item.Automation.LastOrDefault(x => x.Time <= rawStart)?.State ?? item.State;
                if (rawStart > item.Note.Time && item.Bends.FirstOrDefault(x => x.Time == rawStart) is { } bend) currentBend = bend.Value;
                var segmentStart = Quantize(Scale(rawStart, ppq), grid);
                var segmentEnd = Math.Max(segmentStart + Math.Max(1, grid), Quantize(Scale(rawEnd, ppq), grid));
                var pitch = Math.Clamp((int)item.Note.NoteNumber + (item.Note.Channel == 9 ? 0 : spec.Transpose) +
                    (int)Math.Round((currentBend - 8192) / 8192d * segmentState.PitchBendRange, MidpointRounding.AwayFromZero), 0, 127);
                return new NoteEvent(segmentStart, segmentEnd - segmentStart, pitch, item.Note.Velocity, role,
                    item.Track, item.Note.Channel, segmentState.Program, segmentState.Bank, segmentState.Pan, segmentState.Expression,
                    currentBend, segmentState.PitchBendRange,
                    item.Bends.Count == 0 ? null : item.Bends.Select(x => new PitchBendPoint(Scale(x.Time, ppq), x.Value)).ToArray(),
                    Volume: segmentState.Volume, Modulation: segmentState.Modulation, Aftertouch: segmentState.Aftertouch,
                    ReleaseVelocity: item.Note.OffVelocity);
            });
        }).OrderBy(x => x.StartTick).ThenBy(x => x.Role).ToArray();

        return new Song(result, tempo, new MidiMetadata(names, signatures, keys));
    }

    private static long Scale(long tick, int ppq) => checked(tick * TempoMap.Ppq / ppq);
    private static long Quantize(long tick, long grid) => grid <= 0 ? Math.Max(0, tick) : Math.Max(0, (long)Math.Round(tick / (double)grid, MidpointRounding.AwayFromZero) * grid);
    private static bool IsLeadProgram(int program) => program is >= 80 and <= 87 or >= 56 and <= 63 or >= 64 and <= 71;
    private static bool IsBassProgram(int program) => program is >= 32 and <= 39;

    private sealed record ImportedNote(int Track, Note Note, ChannelSnapshot State, long End,
        IReadOnlyList<PitchBendEventData> Bends, IReadOnlyList<AutomationPoint> Automation);
    private sealed record PitchBendEventData(long Time, int Value);
    private sealed record AutomationPoint(long Time, ChannelSnapshot State);
    private sealed record ChannelSnapshot(int Program, int Bank, int Pan, int Expression, int PitchBend, int PitchBendRange,
        int Volume, int Modulation, int Aftertouch);

    private sealed class StateTable(TimedEvent[] events)
    {
        private readonly Dictionary<int, MutableState> _states = [];
        private int _nextEvent;
        private readonly List<TimedEvent> _channelEvents = events.Where(x => x.Event is ChannelEvent).OrderBy(x => x.Time).ToList();

        public void AdvanceTo(long time)
        {
            while (_nextEvent < _channelEvents.Count && _channelEvents[_nextEvent].Time <= time)
            {
                var timed = _channelEvents[_nextEvent++];
                var channel = ((ChannelEvent)timed.Event).Channel;
                Apply(channel, timed.Event);
            }
        }

        public ChannelSnapshot Snapshot(int channel)
        {
            var state = _states.GetValueOrDefault(channel) ?? new MutableState();
            return new(state.Program, state.Bank, state.Pan, state.Expression, state.PitchBend, state.PitchBendRange,
                state.Volume, state.Modulation, state.Aftertouch);
        }

        public long SustainExtension(int channel, long end)
        {
            var down = _states.GetValueOrDefault(channel)?.Sustain ?? false;
            foreach (var timed in _channelEvents.Where(x => ((ChannelEvent)x.Event).Channel == channel && x.Time >= end))
            {
                if (timed.Event is not ControlChangeEvent cc || cc.ControlNumber != 64) continue;
                if (cc.ControlValue >= 64) down = true;
                else if (down) return timed.Time;
            }
            return end;
        }

        public IReadOnlyList<PitchBendEventData> Bends(int channel, long start, long end) => _channelEvents
            .Where(x => ((ChannelEvent)x.Event).Channel == channel && x.Time > start && x.Time < end && x.Event is PitchBendEvent)
            .Select(x => new PitchBendEventData(x.Time, ((PitchBendEvent)x.Event).PitchValue)).ToArray();

        public IReadOnlyList<AutomationPoint> Automation(int channel, long start, long end) => _channelEvents
            .Where(x => ((ChannelEvent)x.Event).Channel == channel && x.Time > start && x.Time < end &&
                x.Event is ControlChangeEvent or ChannelAftertouchEvent or NoteAftertouchEvent)
            .GroupBy(x => x.Time).OrderBy(x => x.Key)
            .Select(x => new AutomationPoint(x.Key, SnapshotAt(channel, x.Key))).ToArray();

        private ChannelSnapshot SnapshotAt(int channel, long time)
        {
            var state = new MutableState();
            foreach (var timed in _channelEvents.Where(x => ((ChannelEvent)x.Event).Channel == channel && x.Time <= time))
                Apply(state, timed.Event);
            return new(state.Program, state.Bank, state.Pan, state.Expression, state.PitchBend, state.PitchBendRange,
                state.Volume, state.Modulation, state.Aftertouch);
        }

        private void Apply(int channel, MidiEvent midiEvent)
        {
            if (!_states.TryGetValue(channel, out var state))
                _states[channel] = state = new MutableState();
            Apply(state, midiEvent);
        }

        private static void Apply(MutableState state, MidiEvent midiEvent)
        {
            switch (midiEvent)
            {
                case ProgramChangeEvent program: state.Program = program.ProgramNumber; break;
                case PitchBendEvent bend: state.PitchBend = bend.PitchValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 0: state.Bank = (cc.ControlValue << 7) | (state.Bank & 127); break;
                case ControlChangeEvent cc when cc.ControlNumber == 32: state.Bank = (state.Bank & (127 << 7)) | cc.ControlValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 10: state.Pan = cc.ControlValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 11: state.Expression = cc.ControlValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 1: state.Modulation = cc.ControlValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 7: state.Volume = cc.ControlValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 64: state.Sustain = cc.ControlValue >= 64; break;
                case ChannelAftertouchEvent aftertouch: state.Aftertouch = aftertouch.AftertouchValue; break;
                case NoteAftertouchEvent aftertouch: state.Aftertouch = aftertouch.AftertouchValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 101: state.RpnMsb = cc.ControlValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 100: state.RpnLsb = cc.ControlValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 6 && state.RpnMsb == 0 && state.RpnLsb == 0: state.PitchBendRange = Math.Clamp((int)cc.ControlValue, 0, 24); break;
            }
        }

        private sealed class MutableState
        {
            public int Program, Bank, Pan = 64, Expression = 127, Volume = 127, Modulation, Aftertouch,
                PitchBend = 8192, PitchBendRange = 2, RpnMsb = 127, RpnLsb = 127;
            public bool Sustain;
        }
    }
}
