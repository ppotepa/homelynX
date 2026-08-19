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
        await process.StandardInput.BaseStream.WriteAsync(wav,ct);process.StandardInput.Close();
        using var output=new MemoryStream();var copy=process.StandardOutput.BaseStream.CopyToAsync(output,ct);var error=process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(copy,process.WaitForExitAsync(ct),error);
        if(process.ExitCode!=0)throw new InvalidOperationException(error.Result.Trim());
        return output.ToArray();
    }
}
