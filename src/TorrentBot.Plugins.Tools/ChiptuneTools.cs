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
        if (string.IsNullOrWhiteSpace(input))
            return new(true, null, "Usage: /chiptune notes=\"C4/8 E4/8 G4/4\" | degrees=\"1/8 3/8 5/4\" key=D scale=minor | generate=song seed=42 chip=genesis");

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
        // Sessions created by older builds may not contain Format. Keep the
        // user-facing default stable: chiptune is delivered as MP3 unless a
        // format was explicitly requested.
        if (string.IsNullOrWhiteSpace(spec.Format)) spec = spec with { Format = "mp3" };
        var song = ChiptuneParser.Compose(spec);
        progress?.Report("chiptune:composed", $"{song.Notes.Count} notes, {song.DurationSeconds:F1}s");
        var hardware = VoiceAllocator.Allocate(song, spec);
        progress?.Report("chiptune:rendering", $"backend={(Environment.GetEnvironmentVariable("CHIPTUNE_RENDERER_PATH") is null ? "managed" : "furnace")}");
        var waitSeconds = ReadInt("TORRENTBOT_CHIPTUNE_RENDER_TIMEOUT_SECONDS", 600, 5, 3600);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(waitSeconds));
        await RenderGate.WaitAsync(timeout.Token);
        byte[] wav;
        try { wav = await FurnaceChipRenderer.RenderAsync(hardware, timeout.Token); }
        finally { RenderGate.Release(); }
        var output = await AudioEncoder.EncodeAsync(wav, spec.Format, timeout.Token);
        progress?.Report("chiptune:encoded", $"mp3={output.Length / 1024} KiB");
        var type = spec.Format switch { "mp3"=>"audio/mpeg", "ogg"=>"audio/ogg", "flac"=>"audio/flac", _=>"audio/wav" };
        var token=Token();
        await store.SaveChiptuneSession(token,user,chat,JsonSerializer.Serialize(spec));
        return FeatureArtifacts.Binary($"chiptune.{spec.Format}", type, output,
            $"Chiptune generated: {spec.Chip}, {song.DurationSeconds:F1}s, {song.Notes.Count} notes, seed={spec.Seed}.",Actions(token,spec));
    }

    private static ChiptuneSpec ApplyAction(ChiptuneSpec spec,string action)=>action switch
    {
        "o-" when spec.Mode is ChiptuneMode.Notes or ChiptuneMode.Midi=>spec with{Transpose=Math.Max(-24,spec.Transpose-12)},
        "o+" when spec.Mode is ChiptuneMode.Notes or ChiptuneMode.Midi=>spec with{Transpose=Math.Min(24,spec.Transpose+12)},
        "o-"=>spec with{Octave=Math.Max(0,spec.Octave-1)}, "o+"=>spec with{Octave=Math.Min(8,spec.Octave+1)},
        "t-"=>spec with{Transpose=Math.Max(-24,spec.Transpose-1)}, "t+"=>spec with{Transpose=Math.Min(24,spec.Transpose+1)},
        "b-"=>spec with{Bpm=Math.Max(40,spec.Bpm-10),TempoMode="override"}, "b+"=>spec with{Bpm=Math.Min(300,spec.Bpm+10),TempoMode="override"},
        "var" when spec.Mode==ChiptuneMode.Generate=>spec with{Seed=unchecked(spec.Seed+1)},
        "chip"=>spec with{Chip=Next(["gameboy","nes","snes","sms","c64_6581","c64_8580","genesis","pce","atari2600","pokey","pcspeaker","zx_spectrum"],spec.Chip)},
        "ins"=>spec with{Instrument=Next(["lead","soft_lead","bass","pluck","arp","bell"],spec.Instrument)},
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
    private static string Next(string[] values,string current){var i=Array.IndexOf(values,current);return values[(i+1+values.Length)%values.Length];}
    private static string Token()=>Convert.ToBase64String(RandomNumberGenerator.GetBytes(9)).TrimEnd('=').Replace('+','-').Replace('/','_');

    private static int ReadInt(string name,int fallback,int min,int max)=>int.TryParse(Environment.GetEnvironmentVariable(name),out var value)?Math.Clamp(value,min,max):fallback;
}
