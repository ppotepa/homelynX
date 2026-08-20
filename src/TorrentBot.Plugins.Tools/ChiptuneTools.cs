using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Plugins.Tools.Chiptune;
using System.Security.Cryptography;
using System.Text.Json;

namespace TorrentBot.Plugins.Tools;

internal static class ChiptuneTools
{
    private static readonly SemaphoreSlim RenderGate = new(1, 1);

    public static async Task<CapabilityResult> ExecuteAsync(string input, string user, string chat, ToolsStore store, IProgressReporter? progress, CancellationToken ct)
    {
        if (input.TrimStart().StartsWith("inspect", StringComparison.OrdinalIgnoreCase))
            return Inspect(input["inspect".Length..].Trim());
        if (input.TrimStart().StartsWith("instruments", StringComparison.OrdinalIgnoreCase))
            return Instruments(input["instruments".Length..].Trim());
        if (string.IsNullOrWhiteSpace(input))
            return new(true, null, "Usage: /chiptune generate=song style=happy chip=nes seed=42 | generate=melody | notes=\"C4/8 E4/8 G4/4\" | attach MIDI. Use /chiptune instruments chip=nes to list instrument controls.");

        ChiptuneSpec spec;
        if(input.StartsWith("callback=",StringComparison.OrdinalIgnoreCase))
        {
            var callback=input["callback=".Length..].Split(':',2);
            if(callback.Length!=2)throw new FormatException("Invalid chiptune callback.");
            var json=await store.GetChiptuneSession(callback[0],user,chat)??throw new InvalidOperationException("This chiptune session expired or belongs to another user.");
            spec=JsonSerializer.Deserialize<ChiptuneSpec>(json)??throw new InvalidOperationException("Stored chiptune session is invalid.");
            spec=ApplyAction(spec,callback[1]);
        }
        else spec = ChiptuneParser.Parse(input);
        progress?.Report("chiptune:parsed", $"chip={spec.Chip}, format={spec.Format}");
        if (string.IsNullOrWhiteSpace(spec.Format)) spec = spec with { Format = "mp3" };
        var song = ChiptuneParser.Compose(spec);
        spec = AutoProfileResolver.Resolve(spec, song);
        song = ArrangementPlanner.Plan(song, spec);
        var sections = string.Join(",", song.Notes.Select(x => x.Section).Distinct(StringComparer.OrdinalIgnoreCase));
        progress?.Report("chiptune:profile", $"chip={spec.Chip}, fidelity={spec.Fidelity}, explicit={spec.ChipExplicit}");
        progress?.Report("chiptune:planned", $"sections={sections}, palette={ArrangementPlanner.DescribePalette(spec.Chip, spec.Style)}");
        progress?.Report("chiptune:composed", $"{song.Notes.Count} notes, {song.DurationSeconds:F1}s");
        var hardware = VoiceAllocator.Allocate(song, spec);
        progress?.Report("chiptune:arranged", $"{hardware.Notes.Count}/{song.Notes.Count} notes, voices={hardware.Notes.Select(x => x.Voice).Distinct().Count()}, revoiced={hardware.RevoicedNotes}, arp={hardware.ArpeggiatedNotes}, dropped={hardware.DroppedNotes}, fidelity={hardware.Fidelity}");
        progress?.Report("chiptune:rendering", $"backend={(Environment.GetEnvironmentVariable("CHIPTUNE_RENDERER_PATH") is null ? "managed" : "furnace")}");
        var waitSeconds = ReadInt("TORRENTBOT_CHIPTUNE_RENDER_TIMEOUT_SECONDS", 600, 5, 3600);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(waitSeconds));
        await RenderGate.WaitAsync(timeout.Token);
        byte[] wav;
        try { wav = await FurnaceChipRenderer.RenderAsync(hardware, timeout.Token); }
        finally { RenderGate.Release(); }
        var output = await AudioEncoder.EncodeAsync(wav, spec.Format, timeout.Token);
        progress?.Report("chiptune:encoded", $"{spec.Format}={output.Length / 1024} KiB");
        var type = spec.Format switch { "mp3"=>"audio/mpeg", "ogg"=>"audio/ogg", "flac"=>"audio/flac", _=>"audio/wav" };
        var token=Token();
        await store.SaveChiptuneSession(token,user,chat,JsonSerializer.Serialize(spec));
        var paletteSummary = string.Join(", ", hardware.Notes.Where(x => x.Role != TrackRole.Drums)
            .GroupBy(x => x.Role).OrderBy(x => x.Key)
            .Select(x => $"{x.Key}:{string.Join("/", x.Select(n => n.Instrument).Distinct())}"));
        return FeatureArtifacts.Binary($"chiptune.{spec.Format}", type, output,
            $"Chiptune generated: {spec.Chip}, {song.DurationSeconds:F1}s, {song.Notes.Count} notes, sections={sections}, instruments={paletteSummary}, seed={spec.Seed}.",Actions(token,spec));
    }

    private static CapabilityResult Instruments(string input)
    {
        var chip = NormalizeChip(ReadOption(input, "chip", "nes"));
        var style = ReadOption(input, "style", "happy").ToLowerInvariant();
        if (!ChiptuneParser.AvailableChips.Contains(chip))
            return new(false, Message: $"Unknown chip '{chip}'. Available: {string.Join(", ", ChiptuneParser.AvailableChips)}");
        if (!ChiptuneParser.AvailableStyles.Contains(style))
            return new(false, Message: $"Unknown style '{style}'. Available: {string.Join(", ", ChiptuneParser.AvailableStyles)}");
        var message = string.Join('\n',
            $"Chiptune instruments for chip={chip}, style={style}",
            $"Auto palette: {ArrangementPlanner.DescribePalette(chip, style)}",
            $"Available patches: {string.Join(", ", ChiptuneParser.AvailableInstruments)}",
            "Overrides: instrument=<patch> or lead=<patch> counter=<patch> bass=<patch> harmony=<patch> arp=<patch> drums=<patch>",
            "Compact form: instruments=\"lead:soft_lead,counter:bell,bass:bass,arp:pluck\"",
            "Register control: register=auto|off chorus_lift=0..24");
        return new(true, null, message);
    }

    private static CapabilityResult Inspect(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new(true, null, "Usage: attach a MIDI file and send /chiptune inspect");
        var spec = ChiptuneParser.Parse(input);
        if (spec.Mode != ChiptuneMode.Midi)
            return new(false, Message: "Inspection currently requires an attached MIDI file.");
        var rawSong = ChiptuneParser.Compose(spec);
        spec = AutoProfileResolver.Resolve(spec, rawSong);
        var song = ArrangementPlanner.Plan(rawSong, spec);
        var hardware = VoiceAllocator.Allocate(song, spec);
        var sourceParts = rawSong.MidiMetadata?.SourceParts is { Count: > 0 } parts
            ? string.Join('\n', parts.Select(x => $"  {x.Name}: track {x.Track + 1}/ch {x.Channel + 1}, program={x.Program}, bank={x.Bank}, role={x.Role}, notes={x.NoteCount}, peak={x.PeakPolyphony}"))
            : "  (none)";
        var peakPolyphony = PeakOverlap(song.Notes);
        var metadata = rawSong.MidiMetadata;
        var names = metadata is null || metadata.TrackNames.Count == 0
            ? "  (none)"
            : string.Join('\n', metadata.TrackNames.OrderBy(x => x.Key).Select(x => $"  track {x.Key + 1}: {x.Value}"));
        var meters = metadata is null || metadata.TimeSignatures.Count == 0
            ? "  (default 4/4)"
            : string.Join(", ", metadata.TimeSignatures.Select(x => $"{x.Numerator}/{x.Denominator}@{x.Tick}"));
        var bendSegments = song.Notes.Sum(x => x.PitchBends?.Count ?? 0);
        var volumeSegments = song.Notes.Sum(x => x.ControllerChanges?.Count(p => p.Volume != 127) ?? 0);
        var modulationSegments = song.Notes.Sum(x => x.ControllerChanges?.Count(p => p.Modulation != 0) ?? 0);
        var aftertouchSegments = song.Notes.Sum(x => x.ControllerChanges?.Count(p => p.Aftertouch != 0) ?? 0);
        var releaseVelocityNotes = song.Notes.Count(x => x.ReleaseVelocity != 0);
        var sections = string.Join('\n', song.Notes.GroupBy(x => x.Section).Select(group =>
        {
            var melodic = group.Where(x => x.Role != TrackRole.Drums).ToArray();
            var range = melodic.Length == 0 ? "n/a" : $"{melodic.Min(x => x.Pitch)}..{melodic.Max(x => x.Pitch)}";
            return $"  {group.Key}: notes={group.Count()}, intensity={group.Average(x => x.SectionIntensity):F2}, pitch={range}";
        }));
        var orchestration = string.Join('\n', hardware.Notes.GroupBy(x => (x.Role, x.Instrument, x.VoiceClass)).OrderBy(x => x.Key.Role).Select(group =>
            $"  {group.Key.Role}: {group.Key.Instrument}/{group.Key.VoiceClass}, voices={string.Join(",", group.Select(x => x.Voice).Distinct().OrderBy(x => x))}, pitch={group.Min(x => x.Pitch)}..{group.Max(x => x.Pitch)}, notes={group.Count()}"));
        var message = string.Join('\n', new[]
        {
            $"MIDI inspection: notes={song.Notes.Count}, duration={song.DurationSeconds:F2}s, real peak polyphony={peakPolyphony}, auto-chip={spec.Chip}",
            $"Target={spec.Chip}, fidelity={spec.Fidelity}, arranged={hardware.Notes.Count}, voices={hardware.Notes.Select(x => x.Voice).Distinct().Count()}, revoiced={hardware.RevoicedNotes}, arpeggiated={hardware.ArpeggiatedNotes}, dropped={hardware.DroppedNotes}",
            $"Auto palette: {ArrangementPlanner.DescribePalette(spec.Chip, spec.Style)}",
            $"Performance data: pitch-bend segments={bendSegments}, CC7 volume segments={volumeSegments}, modulation segments={modulationSegments}, aftertouch segments={aftertouchSegments}, release velocity notes={releaseVelocityNotes}",
            "Detected sections:", sections,
            "Final orchestration:", orchestration,
            "Source parts:", sourceParts,
            "Track names:", names,
            $"Time signatures: {meters}",
            $"Key signatures: {metadata?.KeySignatures.Count ?? 0}"
        });
        return new(true, null, message);
    }

    private static ChiptuneSpec ApplyAction(ChiptuneSpec spec,string action)=>action switch
    {
        "o-" when spec.Mode is ChiptuneMode.Notes or ChiptuneMode.Midi=>spec with{Transpose=Math.Max(-24,spec.Transpose-12)},
        "o+" when spec.Mode is ChiptuneMode.Notes or ChiptuneMode.Midi=>spec with{Transpose=Math.Min(24,spec.Transpose+12)},
        "o-"=>spec with{Octave=Math.Max(0,spec.Octave-1)}, "o+"=>spec with{Octave=Math.Min(8,spec.Octave+1)},
        "t-"=>spec with{Transpose=Math.Max(-24,spec.Transpose-1)}, "t+"=>spec with{Transpose=Math.Min(24,spec.Transpose+1)},
        "b-"=>spec with{Bpm=Math.Max(40,spec.Bpm-10),TempoMode="override"}, "b+"=>spec with{Bpm=Math.Min(300,spec.Bpm+10),TempoMode="override"},
        "var" when spec.Mode==ChiptuneMode.Generate=>spec with{Seed=unchecked(spec.Seed+1)},
        "chip"=>spec with{Chip=Next(["gb","gbc","nes","snes","sms","c64_6581","c64_8580","genesis","pce","atari2600","pokey","pcspeaker","zx_spectrum"],spec.Chip),ChipExplicit=true},
        "ins"=>spec with{Instrument=Next(["auto","lead","soft_lead","pluck","bell","brass","organ","epiano","strings"],spec.Instrument)},
        "x2"=>spec with{Repeat=Math.Min(8,spec.Repeat*2)},
        _=>throw new FormatException("Unsupported chiptune action.")
    };

    private static IReadOnlyList<Dictionary<string,object?>> Actions(string token,ChiptuneSpec spec)
    {
        var actions=new List<Dictionary<string,object?>>();
        void Add(string label,string action)=>actions.Add(new(){{"text",label},{"callbackData",$"ct:{token}:{action}"}});
        Add("Octave -1","o-");Add("Octave +1","o+");Add("Semitone -1","t-");Add("Semitone +1","t+");Add("BPM -10","b-");Add("BPM +10","b+");
        if(spec.Mode==ChiptuneMode.Generate)Add("Variation","var");
        Add("Next instrument","ins");Add("Next chip","chip");Add("Repeat x2","x2");return actions;
    }

    private static string ReadOption(string input, string key, string fallback)
    {
        foreach (var token in input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = token.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals(key, StringComparison.OrdinalIgnoreCase)) return pair[1].Trim('"', '\'');
        }
        return fallback;
    }

    private static string NormalizeChip(string chip) => chip.ToLowerInvariant() switch
    {
        "gameboy" or "dmg" => "gb",
        "gameboy_color" or "color" => "gbc",
        "sega" => "sms",
        "c64" or "sid" => "c64_6581",
        "genesis_fm" or "megadrive" => "genesis",
        "pc_engine" or "turbografx" => "pce",
        "atari" or "tia" => "atari2600",
        "spectrum" or "zx" => "zx_spectrum",
        _ => chip.ToLowerInvariant()
    };

    private static string Next(string[] values,string current){var i=Array.IndexOf(values,current);return values[(i+1+values.Length)%values.Length];}
    private static string Token()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(9)).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static int ReadInt(string name,int fallback,int min,int max)=>int.TryParse(Environment.GetEnvironmentVariable(name),out var value)?Math.Clamp(value,min,max):fallback;

    private static int PeakOverlap(IReadOnlyList<NoteEvent> notes)
    {
        var events = notes.SelectMany(note => new[]
        {
            (Tick: note.StartTick, Delta: 1),
            (Tick: note.EndTick, Delta: -1)
        }).OrderBy(x => x.Tick).ThenBy(x => x.Delta).ToArray();
        var active = 0; var peak = 0;
        foreach (var item in events) { active += item.Delta; peak = Math.Max(peak, active); }
        return peak;
    }
}
