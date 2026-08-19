using System.Diagnostics;

namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class AudioEncoder
{
    public static async Task<byte[]> EncodeAsync(byte[] wav, string format, CancellationToken ct)
    {
        if (format == "wav") return wav;
        var (codec, muxer, args) = format switch
        {
            "mp3" => ("libmp3lame", "mp3", new[] { "-b:a", "192k" }),
            "ogg" => ("libvorbis", "ogg", new[] { "-q:a", "5" }),
            "flac" => ("flac", "flac", Array.Empty<string>()),
            _ => throw new FormatException($"Unknown format '{format}'. Available: wav, mp3, ogg, flac.")
        };
        var path = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";
        var start = new ProcessStartInfo(path) { RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true, UseShellExecute=false, CreateNoWindow=true };
        foreach(var arg in new[]{"-hide_banner","-loglevel","error","-f","wav","-i","pipe:0","-c:a",codec}.Concat(args).Concat(["-f",muxer,"pipe:1"])) start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new InvalidOperationException("ffmpeg is not installed.");
        using var output=new MemoryStream();
        var copyOutput=process.StandardOutput.BaseStream.CopyToAsync(output,ct);
        var readError=process.StandardError.ReadToEndAsync(ct);
        try
        {
            // ffmpeg reads WAV and writes the encoded stream concurrently. Reading
            // stdout only after writing all input deadlocks once either OS pipe fills.
            var writeInput=WriteInputAsync(process.StandardInput.BaseStream,wav,ct);
            await Task.WhenAll(writeInput,copyOutput,readError,process.WaitForExitAsync(ct));
        }
        catch
        {
            if(!process.HasExited)process.Kill(entireProcessTree:true);
            throw;
        }
        if(process.ExitCode!=0)throw new InvalidOperationException(readError.Result.Trim());
        return output.ToArray();
    }

    private static async Task WriteInputAsync(Stream input,byte[] wav,CancellationToken ct)
    {
        try { await input.WriteAsync(wav,ct); }
        finally { await input.DisposeAsync(); }
    }
}
