using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class FurnaceChipRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<byte[]> RenderAsync(HardwareSong song, CancellationToken ct)
    {
        var configured = Environment.GetEnvironmentVariable("CHIPTUNE_RENDERER_PATH");
        if (string.IsNullOrWhiteSpace(configured)) return ManagedChipRenderer.Render(song);
        if (!File.Exists(configured)) throw new InvalidOperationException($"Chiptune renderer is unavailable at '{configured}'. Rebuild the homelynx-bot image.");

        var prepared = PrepareForNative(song);
        var tempDir = Path.Combine(Path.GetTempPath(), "homelynx-chiptune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var outputPath = Path.Combine(tempDir, "render.wav");
        try
        {
            var start = new ProcessStartInfo(configured, outputPath)
            {
                RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true,
                UseShellExecute=false, CreateNoWindow=true, WorkingDirectory=tempDir
            };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the chiptune renderer.");
            try
            {
                await JsonSerializer.SerializeAsync(process.StandardInput.BaseStream, prepared, JsonOptions, ct);
                process.StandardInput.Close();
                var stdout = process.StandardOutput.ReadToEndAsync(ct);
                var stderr = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                await Task.WhenAll(stdout, stderr);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Hardware chiptune rendering failed: {Compact(stderr.Result)}");
                if (!File.Exists(outputPath))
                    throw new InvalidOperationException("Hardware chiptune renderer did not create an audio file.");

                var report = ParseNativeReport(stdout.Result);
                if (!report.Success)
                    throw new InvalidOperationException("Hardware chiptune renderer returned an unsuccessful completion report.");
                if (report.NotesReceived != prepared.Notes.Count || report.NotesWritten != prepared.Notes.Count)
                    throw new InvalidOperationException(
                        $"Hardware chiptune renderer lost note onsets: expected={prepared.Notes.Count}, received={report.NotesReceived}, written={report.NotesWritten}.");

                return await File.ReadAllBytesAsync(outputPath, ct);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree:true);
                throw new TimeoutException("Hardware chiptune rendering exceeded its time limit.");
            }
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    public static async Task<string> ProbeAsync(CancellationToken ct=default)
    {
        var path=Environment.GetEnvironmentVariable("CHIPTUNE_RENDERER_PATH");
        if(string.IsNullOrWhiteSpace(path))return "managed-development";
        if(!File.Exists(path))return "unavailable";
        var start=new ProcessStartInfo(path,"--version"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
        using var process=Process.Start(start)!;var output=await process.StandardOutput.ReadToEndAsync(ct);await process.WaitForExitAsync(ct);
        return process.ExitCode==0?output.Trim():"unavailable";
    }

    private static NativeRenderReport ParseNativeReport(string stdout)
    {
        // Furnace may emit console diagnostics in development builds. The
        // adapter's completion report is the final JSON object on stdout.
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            if (!line.StartsWith('{')) continue;
            try
            {
                var report = JsonSerializer.Deserialize<NativeRenderReport>(line, JsonOptions);
                if (report is not null) return report;
            }
            catch (JsonException) { }
        }
        throw new InvalidOperationException($"Hardware chiptune renderer returned no valid completion report: {Compact(stdout)}");
    }

    private static string Compact(string value)
    {
        value=value.Trim();if(value.Length==0)return "unknown renderer error";
        return value.Length<=500?value:value[..500]+"…";
    }

    private static HardwareSong PrepareForNative(HardwareSong song)
    {
        var normalized = NormalizeTimeline(song);

        // Public/internal InstrumentIds describe stable semantic patches and
        // may be sparse (for example drum or Genesis PSG ranges). Furnace's
        // pattern instrument column is an index into the instrument vector, so
        // compact only the patches actually used by this render to 0..N-1.
        var keys = normalized.Notes
            .OrderBy(x => x.InstrumentId)
            .ThenBy(x => x.Instrument, StringComparer.Ordinal)
            .ThenBy(x => x.VoiceClass, StringComparer.Ordinal)
            .Select(x => (x.Instrument, x.VoiceClass))
            .Distinct()
            .ToArray();
        if (keys.Length >= 180)
            throw new InvalidOperationException($"Chiptune requires {keys.Length} instruments; Furnace pattern capacity is 180.");

        var ids = keys.Select((key, index) => (key, index)).ToDictionary(x => x.key, x => x.index);
        var notes = normalized.Notes
            .Select(note => note with { InstrumentId = ids[(note.Instrument, note.VoiceClass)] })
            .ToArray();
        return normalized with { Notes = notes };
    }

    private static HardwareSong NormalizeTimeline(HardwareSong song)
    {
        var sourceTempo = new TempoMap(song.Tempo);
        var ticksPerSecond = song.Bpm * TempoMap.Ppq / 60d;
        long ToFixedTick(long tick) => (long)Math.Round(sourceTempo.TickToSeconds(tick) * ticksPerSecond);

        var notes = song.Notes.Select(note =>
        {
            var start = ToFixedTick(note.StartTick);
            var end = ToFixedTick(note.StartTick + note.DurationTick);
            var bends = note.PitchBends?.Select(point => point with { Tick = ToFixedTick(point.Tick) }).ToArray();
            var controllers = note.ControllerChanges?.Select(point => point with { Tick = ToFixedTick(point.Tick) }).ToArray();
            return note with { StartTick = start, DurationTick = Math.Max(1, end - start), PitchBends = bends, ControllerChanges = controllers };
        }).ToArray();

        return song with
        {
            Tempo = TempoMap.Fixed(song.Bpm).Points,
            Notes = notes,
            EndTick = ToFixedTick(song.EndTick)
        };
    }

    private sealed record NativeRenderReport(
        bool Success,
        string? Backend,
        int NotesReceived,
        int NotesWritten,
        int StartRowsAdjusted,
        int NoteOffsSuppressed);
}
