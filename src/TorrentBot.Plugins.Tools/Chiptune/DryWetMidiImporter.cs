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
        for (var trackIndex = 0; trackIndex < timedByTrack.Length; trackIndex++)
        {
            foreach (var timed in timedByTrack[trackIndex])
            {
                switch (timed.Event)
                {
                    case SequenceTrackNameEvent name when !string.IsNullOrWhiteSpace(name.Text):
                        names.TryAdd(trackIndex, name.Text.Trim());
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
            var trackEnd = Math.Max(
                events.Length == 0 ? 0 : events.Max(x => x.Time),
                notes.Length == 0 ? 0 : notes.Max(x => x.Time + x.Length));
            var state = new StateTable(events);
            foreach (var note in notes)
            {
                state.AdvanceTo(note.Time);
                var naturalKeyRelease = note.Time + note.Length;
                var keyRelease = state.ChannelModeKeyRelease(note.Channel, note.Time, naturalKeyRelease);
                var soundingEnd = state.SustainExtension(note.Channel, keyRelease, trackEnd);
                soundingEnd = state.AllSoundOff(note.Channel, note.Time, soundingEnd);
                var bends = state.Bends(note.Channel, note.Time, soundingEnd);
                var automation = state.Automation(note.Channel, (int)note.NoteNumber, note.Time, soundingEnd);
                completed.Add(new ImportedNote(trackIndex, note, state.Snapshot(note.Channel), keyRelease, soundingEnd, bends, automation));
            }
        }

        if (completed.Count == 0) throw new InvalidDataException("MIDI file contains no notes.");

        var stats = completed
            .GroupBy(x => (x.Track, Channel: (int)x.Note.Channel, x.State.Program, x.State.Bank))
            .Select(group => new
            {
                group.Key,
                Median = group.Select(x => (int)x.Note.NoteNumber).Order().ElementAt(group.Count() / 2),
                Count = group.Count(),
                Name = names.GetValueOrDefault(group.Key.Track, string.Empty)
            })
            .ToArray();
        var melodic = stats.Where(x => x.Key.Channel != 9).ToArray();
        var lead = melodic
            .OrderByDescending(x => IsLeadName(x.Name))
            .ThenByDescending(x => IsLeadProgram(x.Key.Program))
            .ThenByDescending(x => x.Median)
            .ThenByDescending(x => x.Count)
            .FirstOrDefault()?.Key;
        var bass = melodic.Length > 1
            ? melodic
                .OrderByDescending(x => IsBassName(x.Name))
                .ThenByDescending(x => IsBassProgram(x.Key.Program))
                .ThenBy(x => x.Median)
                .ThenByDescending(x => x.Count)
                .First().Key
            : ((int Track, int Channel, int Program, int Bank)?)null;

        if (bass is not null && lead is not null && bass.Value == lead.Value && melodic.Length > 1)
        {
            bass = melodic
                .Where(x => x.Key != lead.Value)
                .OrderByDescending(x => IsBassName(x.Name))
                .ThenByDescending(x => IsBassProgram(x.Key.Program))
                .ThenBy(x => x.Median)
                .First().Key;
        }

        var grid = spec.Quantize switch
        {
            "off" => 0L,
            "1/4" => TempoMap.Ppq,
            "1/8" => TempoMap.Ppq / 2,
            "1/32" => TempoMap.Ppq / 8,
            "1/64" => TempoMap.Ppq / 16,
            _ => TempoMap.Ppq / 4
        };

        var result = completed.Select(item =>
        {
            var rawStart = Scale(item.Note.Time, ppq);
            var rawKeyEnd = Scale(item.KeyRelease, ppq);
            var rawEnd = Scale(item.End, ppq);
            var key = (item.Track, Channel: (int)item.Note.Channel, item.State.Program, item.State.Bank);
            var role = item.Note.Channel == 9
                ? TrackRole.Drums
                : key == bass
                    ? TrackRole.Bass
                    : key == lead
                        ? TrackRole.Lead
                        : TrackRole.Harmony;

            var initialState = item.State;
            var pitch = Math.Clamp((int)item.Note.NoteNumber + (item.Note.Channel == 9 ? 0 : spec.Transpose) +
                (int)Math.Round((initialState.PitchBend - 8192) / 8192d * initialState.PitchBendRange, MidpointRounding.AwayFromZero), 0, 127);
            var startTick = Quantize(rawStart, grid);
            var minimumDuration = Math.Max(1, grid);
            var keyEndTick = Math.Max(startTick + minimumDuration, Quantize(rawKeyEnd, grid));
            var endTick = Math.Max(keyEndTick, Quantize(rawEnd, grid));
            return new NoteEvent(startTick, endTick - startTick, pitch, item.Note.Velocity, role,
                item.Track, item.Note.Channel, initialState.Program, initialState.Bank, initialState.Pan, initialState.Expression,
                initialState.PitchBend, initialState.PitchBendRange,
                item.Bends.Count == 0 ? null : item.Bends.Select(x => new PitchBendPoint(Scale(x.Time, ppq), x.Value)).ToArray(),
                Volume: initialState.Volume, Modulation: initialState.Modulation, Aftertouch: initialState.Aftertouch,
                ReleaseVelocity: item.Note.OffVelocity,
                ControllerChanges: item.Automation.Select(x => new ControllerPoint(Scale(x.Time, ppq), x.State.Volume, x.State.Expression, x.State.Pan, x.State.Modulation, x.State.Aftertouch)).ToArray(),
                KeyDurationTick: keyEndTick - startTick);
        }).OrderBy(x => x.StartTick).ThenBy(x => x.Role).ToArray();

        var sourceParts = result
            .Where(x => x.SourceTrack >= 0 && x.SourceChannel >= 0)
            .GroupBy(x => (x.SourceTrack, x.SourceChannel, x.Program, x.Bank))
            .Select(group => new MidiSourcePart(
                group.Key.SourceTrack, group.Key.SourceChannel, group.Key.Program, group.Key.Bank,
                names.GetValueOrDefault(group.Key.SourceTrack, $"Track {group.Key.SourceTrack + 1}"),
                group.First().Role, group.Count(), PeakOverlap(group)))
            .OrderBy(x => x.Track).ThenBy(x => x.Channel).ThenBy(x => x.Program)
            .ToArray();
        return new Song(result, tempo, new MidiMetadata(names, signatures, keys, sourceParts));
    }

    private static long Scale(long tick, int ppq) => checked(tick * TempoMap.Ppq / ppq);
    private static long Quantize(long tick, long grid) => grid <= 0 ? Math.Max(0, tick) : Math.Max(0, (long)Math.Round(tick / (double)grid, MidpointRounding.AwayFromZero) * grid);
    private static bool IsLeadProgram(int program) => program is >= 56 and <= 87;
    private static bool IsBassProgram(int program) => program is >= 32 and <= 39;
    private static bool IsLeadName(string name)
    {
        name = name.ToLowerInvariant();
        return name.Contains("lead") || name.Contains("melody") || name.Contains("solo") || name.Contains("theme");
    }
    private static bool IsBassName(string name) => name.Contains("bass", StringComparison.OrdinalIgnoreCase);

    private static int PeakOverlap(IEnumerable<NoteEvent> notes)
    {
        var active = 0;
        var peak = 0;
        foreach (var point in notes.SelectMany(x => new[]
                     { (Tick: x.StartTick, Delta: 1), (Tick: x.EndTick, Delta: -1) })
                 .OrderBy(x => x.Tick).ThenBy(x => x.Delta))
        {
            active += point.Delta;
            peak = Math.Max(peak, active);
        }
        return peak;
    }

    private sealed record ImportedNote(int Track, Note Note, ChannelSnapshot State, long KeyRelease, long End,
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
            return Snapshot(state);
        }

        public long ChannelModeKeyRelease(int channel, long start, long naturalEnd)
        {
            foreach (var timed in _channelEvents.Where(x => ((ChannelEvent)x.Event).Channel == channel && x.Time > start && x.Time < naturalEnd))
            {
                if (timed.Event is ControlChangeEvent cc && (int)cc.ControlNumber is >= 123 and <= 127)
                    return timed.Time;
            }
            return naturalEnd;
        }

        public long SustainExtension(int channel, long end, long trackEnd)
        {
            var channelEvents = _channelEvents.Where(x => ((ChannelEvent)x.Event).Channel == channel).ToArray();
            var down = false;
            foreach (var timed in channelEvents.Where(x => x.Time <= end))
            {
                if (timed.Event is not ControlChangeEvent cc) continue;
                if (cc.ControlNumber == 64) down = cc.ControlValue >= 64;
                else if (cc.ControlNumber == 121) down = false;
            }
            if (!down) return end;
            foreach (var timed in channelEvents.Where(x => x.Time > end))
            {
                if (timed.Event is not ControlChangeEvent cc) continue;
                if (cc.ControlNumber == 121 || cc.ControlNumber == 64 && cc.ControlValue < 64)
                    return timed.Time;
            }
            return Math.Max(end, trackEnd);
        }

        public long AllSoundOff(int channel, long start, long end)
        {
            var eventTime = _channelEvents
                .Where(x => ((ChannelEvent)x.Event).Channel == channel && x.Time > start && x.Time < end &&
                            x.Event is ControlChangeEvent cc && cc.ControlNumber == 120)
                .Select(x => (long?)x.Time)
                .FirstOrDefault();
            return eventTime ?? end;
        }

        public IReadOnlyList<PitchBendEventData> Bends(int channel, long start, long end) => _channelEvents
            .Where(x => ((ChannelEvent)x.Event).Channel == channel && x.Time > start && x.Time < end && x.Event is PitchBendEvent)
            .Select(x => new PitchBendEventData(x.Time, ((PitchBendEvent)x.Event).PitchValue)).ToArray();

        public IReadOnlyList<AutomationPoint> Automation(int channel, int noteNumber, long start, long end) => _channelEvents
            .Where(x => ((ChannelEvent)x.Event).Channel == channel && x.Time > start && x.Time < end && IsExpressiveEvent(x.Event, noteNumber))
            .GroupBy(x => x.Time)
            .OrderBy(x => x.Key)
            .Select(group =>
            {
                var state = SnapshotAt(channel, group.Key);
                var polyAftertouch = group
                    .Select(x => x.Event)
                    .OfType<NoteAftertouchEvent>()
                    .Where(x => (int)x.NoteNumber == noteNumber)
                    .Select(x => (int)x.AftertouchValue)
                    .LastOrDefault(state.Aftertouch);
                return new AutomationPoint(group.Key, state with { Aftertouch = polyAftertouch });
            }).ToArray();

        private static bool IsExpressiveEvent(MidiEvent midiEvent, int noteNumber) => midiEvent switch
        {
            ControlChangeEvent cc => (int)cc.ControlNumber is 1 or 7 or 10 or 11 or 121,
            ChannelAftertouchEvent => true,
            NoteAftertouchEvent noteAftertouch => (int)noteAftertouch.NoteNumber == noteNumber,
            _ => false
        };

        private ChannelSnapshot SnapshotAt(int channel, long time)
        {
            var state = new MutableState();
            foreach (var timed in _channelEvents.Where(x => ((ChannelEvent)x.Event).Channel == channel && x.Time <= time))
                Apply(state, timed.Event);
            return Snapshot(state);
        }

        private static ChannelSnapshot Snapshot(MutableState state) =>
            new(state.Program, state.Bank, state.Pan, state.Expression, state.PitchBend, state.PitchBendRange,
                state.Volume, state.Modulation, state.Aftertouch);

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
                case ControlChangeEvent cc when cc.ControlNumber == 101: state.RpnMsb = cc.ControlValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 100: state.RpnLsb = cc.ControlValue; break;
                case ControlChangeEvent cc when cc.ControlNumber == 6 && state.RpnMsb == 0 && state.RpnLsb == 0:
                    state.PitchBendRange = Math.Clamp((int)cc.ControlValue, 0, 24);
                    break;
                case ControlChangeEvent cc when cc.ControlNumber == 121:
                    state.Pan = 64;
                    state.Expression = 127;
                    state.Modulation = 0;
                    state.Aftertouch = 0;
                    state.PitchBend = 8192;
                    state.PitchBendRange = 2;
                    state.RpnMsb = 127;
                    state.RpnLsb = 127;
                    state.Sustain = false;
                    break;
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
