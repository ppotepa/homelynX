using TorrentBot.Contracts.Capabilities;
using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Plugins.Tools;

internal static class ChiptuneTools
{
    private static readonly SemaphoreSlim RenderGate = new(1, 1);

    public static async Task<CapabilityResult> ExecuteAsync(string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new(true, null, "Usage: /chiptune notes=\"C4/8 E4/8 G4/4\" | degrees=\"1/8 3/8 5/4\" key=D scale=minor | generate=riff seed=42");

        var spec = ChiptuneParser.Parse(input);
        var song = ChiptuneParser.Compose(spec);
        var hardware = VoiceAllocator.Allocate(song, spec);
        var waitSeconds = ReadInt("TORRENTBOT_CHIPTUNE_RENDER_TIMEOUT_SECONDS", 60, 5, 300);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(waitSeconds));
        await RenderGate.WaitAsync(timeout.Token);
        byte[] wav;
        try { wav = ManagedChipRenderer.Render(hardware); }
        finally { RenderGate.Release(); }
        var output = await AudioEncoder.EncodeAsync(wav, spec.Format, timeout.Token);
        var type = spec.Format switch { "mp3"=>"audio/mpeg", "ogg"=>"audio/ogg", "flac"=>"audio/flac", _=>"audio/wav" };
        return FeatureArtifacts.Binary($"chiptune.{spec.Format}", type, output,
            $"Chiptune generated: {spec.Chip}, {song.DurationSeconds:F1}s, {song.Notes.Count} notes, seed={spec.Seed}.");
    }

    private static int ReadInt(string name,int fallback,int min,int max)=>int.TryParse(Environment.GetEnvironmentVariable(name),out var value)?Math.Clamp(value,min,max):fallback;
}
