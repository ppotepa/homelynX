using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TorrentBot.Plugins.Downloads.Downloaders;

/// <summary>
/// Downloads public media URLs through the yt-dlp and FFmpeg command line tools.
/// The process is intentionally isolated behind IDownloader so torrent jobs keep
/// using the existing qBittorrent integration.
/// </summary>
public sealed class MediaDownloader : IDownloader
{
    private static readonly string[] AllowedHosts =
    [
        "youtube.com", "youtu.be", "facebook.com", "fb.watch", "dailymotion.com", "dai.ly",
        "vimeo.com", "instagram.com", "tiktok.com"
    ];

    private readonly ConcurrentDictionary<string, MediaEntry> _downloads = new(StringComparer.Ordinal);
    private readonly string _ytDlpPath;
    private readonly string _ffmpegPath;
    private readonly string _outputRoot;
    private readonly string _tempRoot;
    private readonly string? _cookiesFile;
    private readonly int _maxDurationSeconds;

    public MediaDownloader(
        string? ytDlpPath = null,
        string? ffmpegPath = null,
        string? outputRoot = null,
        string? tempRoot = null,
        int? maxDurationSeconds = null)
    {
        _ytDlpPath = ytDlpPath ?? Environment.GetEnvironmentVariable("YTDLP_PATH") ?? "yt-dlp";
        _ffmpegPath = ffmpegPath ?? Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "ffmpeg";
        _outputRoot = outputRoot ?? Environment.GetEnvironmentVariable("MEDIA_LIBRARY_PATH") ?? "/media";
        _tempRoot = tempRoot ?? Environment.GetEnvironmentVariable("MEDIA_DOWNLOAD_TEMP_DIR") ?? "/downloads/incomplete/media";
        var configuredCookies = Environment.GetEnvironmentVariable("YTDLP_COOKIES_FILE");
        _cookiesFile = !string.IsNullOrWhiteSpace(configuredCookies) && File.Exists(configuredCookies)
            ? configuredCookies
            : null;
        if (maxDurationSeconds.HasValue)
        {
            _maxDurationSeconds = maxDurationSeconds.Value;
        }
        else if (int.TryParse(Environment.GetEnvironmentVariable("MEDIA_DOWNLOAD_MAX_DURATION_SECONDS"), out var configured))
        {
            _maxDurationSeconds = configured;
        }
        else
        {
            _maxDurationSeconds = 14_400;
        }
    }

    public string Type => "media";
    public string DisplayName => "Media URL (yt-dlp + FFmpeg)";

    public Task<DownloadSearchResults> SearchAsync(DownloadSearchRequest request, CancellationToken ct = default) =>
        Task.FromResult(new DownloadSearchResults([]));

    public async Task<DownloadTicket> StartAsync(DownloadStartRequest request, CancellationToken ct = default)
    {
        var url = ValidateUrl(request.Url);
        var format = ParseFormat(request.MediaFormat);
        var quality = ParseQuality(format, request.MediaQuality);
        var subtitles = ParseSubtitles(request.MediaSubtitles);
        LogProbe(url, "starting");
        MediaProbe probe;
        try
        {
            probe = await ProbeAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogProbe(url, $"failed: {exception.Message}");
            throw;
        }
        var clip = ParseClip(request.MediaClipStart, request.MediaClipEnd, probe.DurationSeconds);

        if (probe.IsLive)
        {
            throw new InvalidOperationException("Transmisje na żywo nie są obsługiwane.");
        }

        if (probe.DurationSeconds is null || probe.DurationSeconds > _maxDurationSeconds)
        {
            throw new InvalidOperationException($"Materiał musi mieć znany czas trwania i nie może przekraczać {_maxDurationSeconds / 3600} godzin.");
        }

        var id = $"media-{Guid.NewGuid():N}";
        var entry = new MediaEntry(
            id,
            probe.Title,
            url,
            format,
            quality,
            clip,
            subtitles,
            "probing",
            0,
            0,
            0,
            null,
            new CancellationTokenSource());
        _downloads[id] = entry;

        _ = Task.Run(() => DownloadAsync(entry, probe), CancellationToken.None);
        return new DownloadTicket(id, Type, probe.Title);
    }

    public Task<DownloadStatus> GetStatusAsync(string downloadId, CancellationToken ct = default)
    {
        if (!_downloads.TryGetValue(downloadId, out var entry))
        {
            throw new KeyNotFoundException($"Media download '{downloadId}' was not found.");
        }

        return Task.FromResult(new DownloadStatus(
            entry.Id,
            Type,
            entry.Name,
            entry.Status,
            entry.Progress,
            entry.SizeBytes,
            entry.DownloadedBytes,
            Category: entry.Format,
            EtaSeconds: null));
    }

    public Task PauseAsync(string downloadId, CancellationToken ct = default) =>
        Task.FromException(new NotSupportedException("Pauza pobierania mediów nie jest obsługiwana."));

    public Task ResumeAsync(string downloadId, CancellationToken ct = default) =>
        Task.FromException(new NotSupportedException("Wznowienie pobierania mediów nie jest obsługiwane."));

    public Task CancelAsync(string downloadId, CancellationToken ct = default)
    {
        if (_downloads.TryRemove(downloadId, out var entry))
        {
            entry.Cancellation.Cancel();
            TryKill(entry.Process);
            TryDelete(entry.TempDirectory);
        }

        return Task.CompletedTask;
    }

    internal IReadOnlyList<Dictionary<string, object?>> GetSnapshotRows() =>
        _downloads.Values.Select(entry => new Dictionary<string, object?>
        {
            ["id"] = entry.Id,
            ["name"] = entry.Name,
            ["provider"] = Type,
            ["status"] = entry.Status,
            ["progress"] = entry.Progress,
            ["size"] = entry.SizeBytes,
            ["downloaded"] = entry.DownloadedBytes,
            ["category"] = entry.Format,
            ["error"] = entry.Error,
            ["outputPath"] = entry.OutputPath,
            ["url"] = entry.Url
        }).ToList();

    private async Task DownloadAsync(MediaEntry entry, MediaProbe probe)
    {
        try
        {
            Log(entry, $"starting provider=media format={entry.Format} quality={entry.Quality}");
            Directory.CreateDirectory(_tempRoot);
            entry.TempDirectory = Path.Combine(_tempRoot, entry.Id);
            Directory.CreateDirectory(entry.TempDirectory);
            Update(entry.Id, e => e with { Status = "downloading" });

            var outputTemplate = Path.Combine(entry.TempDirectory, $"{probe.Id}.%(ext)s");
            var arguments = BuildDownloadArguments(entry, outputTemplate);
            var process = StartProcess(_ytDlpPath, arguments, entry.TempDirectory);
            entry.Process = process;

            var processOutput = await ReadProcessAsync(process, entry).ConfigureAwait(false);
            if (process.ExitCode != 0 || entry.Cancellation.IsCancellationRequested)
            {
                var error = Tail(string.IsNullOrWhiteSpace(processOutput.Error)
                    ? processOutput.StandardOutput
                    : processOutput.Error);
                var message = string.IsNullOrWhiteSpace(error) ? $"yt-dlp zakończył się kodem {process.ExitCode}." : error;
                Log(entry, $"yt-dlp failed exit={process.ExitCode}: {message}");
                Update(entry.Id, e => e with { Error = message });
                Update(entry.Id, e => e with { Status = entry.Cancellation.IsCancellationRequested ? "cancelled" : "failed" });
                return;
            }

            Update(entry.Id, e => e with { Status = "converting", Progress = 0.95 });
            if (entry.Format.Equals("subtitles", StringComparison.OrdinalIgnoreCase))
            {
                var subtitleDestination = MoveSubtitles(entry, probe, null);
                var subtitleSize = new FileInfo(subtitleDestination).Length;
                Update(entry.Id, e => e with
                {
                    Status = "completed",
                    Progress = 1,
                    SizeBytes = subtitleSize,
                    DownloadedBytes = subtitleSize,
                    OutputPath = subtitleDestination,
                    Process = null
                });
                return;
            }

            var source = FindCompletedFile(entry.TempDirectory, probe.Id, entry.Format);
            if (source is null)
            {
                throw new InvalidOperationException("yt-dlp nie utworzył pliku wynikowego.");
            }

            if (entry.Format.Equals("mp4", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureVideoHasAudioAsync(source, entry.Cancellation.Token).ConfigureAwait(false);
            }

            var destinationDirectory = entry.Format.Equals("mp3", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(_outputRoot, "music", "Online")
                : Path.Combine(_outputRoot, "movies", "Online");
            Directory.CreateDirectory(destinationDirectory);
            var extension = entry.Format.Equals("mp3", StringComparison.OrdinalIgnoreCase) ? "mp3" : "mp4";
            var destination = NextAvailablePath(destinationDirectory, SanitizeFileName(probe.Title), probe.Id, entry.Quality, extension, entry.Clip);
            File.Move(source, destination);
            MoveSubtitles(entry, probe, destination);
            var size = new FileInfo(destination).Length;
            Update(entry.Id, e => e with
            {
                Status = "completed",
                Progress = 1,
                SizeBytes = size,
                DownloadedBytes = size,
                OutputPath = destination,
                Process = null
            });
        }
        catch (OperationCanceledException)
        {
            Log(entry, "cancelled");
            Update(entry.Id, e => e with { Status = "cancelled" });
        }
        catch (Exception exception)
        {
            Log(entry, $"failed: {exception.Message}");
            Update(entry.Id, e => e with { Status = "failed", Error = exception.Message });
        }
        finally
        {
            TryDelete(entry.TempDirectory);
            entry.Process?.Dispose();
        }
    }

    private async Task<MediaProbe> ProbeAsync(string url, CancellationToken ct)
    {
        var arguments = new List<string>();
        AddProviderArguments(arguments, url);
        AddCookieArguments(arguments);
        arguments.AddRange(["--dump-single-json", "--no-playlist", "--skip-download", url]);
        using var process = StartProcess(_ytDlpPath, arguments, Directory.GetCurrentDirectory());
        using var cancellation = ct.Register(() => TryKill(process));
        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Nie udało się odczytać materiału." : Tail(error));
        }

        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        var title = root.TryGetProperty("title", out var titleValue) ? titleValue.GetString() : null;
        var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
        var duration = root.TryGetProperty("duration", out var durationValue) && durationValue.ValueKind == JsonValueKind.Number
            ? durationValue.GetDouble()
            : (double?)null;
        var live = root.TryGetProperty("is_live", out var liveValue) && liveValue.ValueKind == JsonValueKind.True;
        var type = root.TryGetProperty("_type", out var typeValue) ? typeValue.GetString() : null;
        if (string.Equals(type, "playlist", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Playlisty nie są obsługiwane — podaj bezpośredni link do materiału.");
        }

        return new MediaProbe(id ?? Guid.NewGuid().ToString("N"), title ?? "media", duration, live);
    }

    private async Task<ProcessOutput> ReadProcessAsync(Process process, MediaEntry entry)
    {
        using var cancellation = entry.Cancellation.Token.Register(() => TryKill(process));
        var stdoutTask = ReadStreamAsync(process.StandardOutput, entry);
        var stderrTask = ReadStreamAsync(process.StandardError, entry);
        var waitTask = process.WaitForExitAsync();
        await Task.WhenAll(stdoutTask, stderrTask, waitTask).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessOutput(stdout, stderr);
    }

    private async Task<string> ReadStreamAsync(StreamReader reader, MediaEntry entry)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            output.AppendLine(line);
            if (line.Contains('%'))
            {
                var percent = line.Split('%')[0].Split(' ').LastOrDefault();
                if (double.TryParse(percent, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                    Update(entry.Id, e => e with { Progress = Math.Clamp(value / 100, 0, 0.95) });
            }
            if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || line.Contains("WARNING", StringComparison.OrdinalIgnoreCase))
                Log(entry, line.Trim());
        }
        return output.ToString();
    }

    private string[] BuildDownloadArguments(MediaEntry entry, string outputTemplate)
    {
        var args = new List<string>
        {
            "--no-playlist", "--newline", "--ffmpeg-location", _ffmpegPath
        };
        AddProviderArguments(args, entry.Url);
        AddCookieArguments(args);
        if (entry.Clip is not null)
        {
            args.AddRange(["--download-sections", $"*{entry.Clip.Start}-{entry.Clip.End}", "--force-keyframes-at-cuts"]);
        }
        if (entry.Subtitles is not null)
        {
            args.AddRange(["--write-subs", "--sub-langs", entry.Subtitles.Languages, "--convert-subs", "srt"]);
            if (entry.Subtitles.IncludeAuto)
            {
                args.Add("--write-auto-subs");
            }
        }

        if (entry.Format.Equals("subtitles", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--skip-download");
        }
        else if (entry.Format.Equals("mp3", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(["-x", "--audio-format", "mp3", "--audio-quality", $"{entry.Quality}K"]);
        }
        else
        {
            var formatSelector = IsTikTokUrl(entry.Url)
                ? $"b[width<={entry.Quality}]/b[height<={entry.Quality}]/b"
                : IsFacebookUrl(entry.Url)
                    ? "hd/sd/b"
                    : $"bv*[height<={entry.Quality}]+ba/b[height<={entry.Quality}]";
            args.AddRange(["-f", formatSelector, "--merge-output-format", "mp4"]);
        }

        args.AddRange(["-o", outputTemplate, entry.Url]);
        return args.ToArray();
    }

    private static void AddProviderArguments(List<string> args, string url)
    {
        if (IsYoutubeUrl(url))
            args.AddRange([
                "--remote-components", "ejs:github"
            ]);
    }

    private void AddCookieArguments(List<string> args)
    {
        if (_cookiesFile is not null)
            args.AddRange(["--cookies", _cookiesFile]);
    }

    private static bool IsYoutubeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase));

    private static string Tail(string value)
    {
        var compact = value.Trim();
        return compact.Length <= 1200 ? compact : compact[^1200..];
    }

    private static void Log(MediaEntry entry, string message) =>
        Console.Error.WriteLine($"media-download id={entry.Id} host={GetHost(entry.Url)} {message}");

    private static void LogProbe(string url, string message) =>
        Console.Error.WriteLine($"media-probe host={GetHost(url)} {message}");

    private static string GetHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "unknown";

    private static bool IsTikTokUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsFacebookUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Host.Equals("facebook.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("fb.watch", StringComparison.OrdinalIgnoreCase));

    private static Process StartProcess(string fileName, IEnumerable<string> arguments, string workingDirectory)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = Process.Start(info) ?? throw new InvalidOperationException($"Nie można uruchomić {fileName}.");
        return process;
    }

    private static string ValidateUrl(string? raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || !AllowedHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Obsługiwane są wyłącznie publiczne URL-e YouTube, Facebook, Dailymotion, Vimeo, Instagram i TikTok.", nameof(raw));
        }

        return uri.ToString();
    }

    private static string ParseFormat(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "mp3" => "mp3",
        "mp4" or null or "" => "mp4",
        "subtitles" or "subs" => "subtitles",
        _ => throw new ArgumentException("Format musi być mp3, mp4 albo subtitles.", nameof(value))
    };

    private static string ParseQuality(string format, string? value)
    {
        if (format == "subtitles")
        {
            return "srt";
        }
        var quality = string.IsNullOrWhiteSpace(value) ? (format == "mp3" ? "192" : "720") : value.Trim().TrimEnd('k', 'K', 'p', 'P');
        var valid = format == "mp3" ? new[] { "128", "192", "320" } : new[] { "360", "480", "720", "1080" };
        return valid.Contains(quality, StringComparer.Ordinal) ? quality : throw new ArgumentException("Nieobsługiwana jakość.", nameof(value));
    }

    private static SubtitleOptions? ParseSubtitles(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var values = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.ToLowerInvariant())
            .ToArray();
        var includeAuto = values.Contains("auto", StringComparer.Ordinal);
        var languages = values.Where(value => value is not "auto" and not "all").ToArray();
        if (languages.Any(language => !System.Text.RegularExpressions.Regex.IsMatch(language, "^[a-z]{2,3}(?:-[a-z]+)?$")))
        {
            throw new ArgumentException("Języki napisów muszą mieć format en, pl lub de.");
        }

        return new SubtitleOptions(languages.Length == 0 ? "all" : string.Join(',', languages), includeAuto);
    }

    private static MediaClip? ParseClip(string? rawStart, string? rawEnd, double? durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(rawStart) && string.IsNullOrWhiteSpace(rawEnd))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawStart) || string.IsNullOrWhiteSpace(rawEnd)
            || !TryParseTimestamp(rawStart, out var start)
            || !TryParseTimestamp(rawEnd, out var end)
            || end <= start)
        {
            throw new ArgumentException("Fragment wymaga poprawnego zakresu, np. clip 00:22 00:33.");
        }

        if (durationSeconds is null || end > TimeSpan.FromSeconds(durationSeconds.Value))
        {
            throw new ArgumentException("Koniec fragmentu wykracza poza długość materiału.");
        }

        return new MediaClip(FormatTimestamp(start), FormatTimestamp(end));
    }

    private static bool TryParseTimestamp(string value, out TimeSpan timestamp)
    {
        timestamp = default;
        var fields = value.Trim().Split(':');
        if (fields.Length is < 1 or > 3 || fields.Any(field => !int.TryParse(field, out _)))
        {
            return false;
        }

        var values = fields.Select(int.Parse).ToArray();
        var seconds = values[^1];
        var minutes = values.Length >= 2 ? values[^2] : 0;
        var hours = values.Length == 3 ? values[0] : 0;
        if (seconds is < 0 or >= 60 || minutes is < 0 or >= 60 || hours < 0)
        {
            return false;
        }

        timestamp = new TimeSpan(hours, minutes, seconds);
        return true;
    }

    private static string FormatTimestamp(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");

    private void Update(string id, Func<MediaEntry, MediaEntry> updater)
    {
        if (_downloads.TryGetValue(id, out var current))
        {
            _downloads[id] = updater(current);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "media" : sanitized[..Math.Min(sanitized.Length, 120)];
    }

    private static string NextAvailablePath(string directory, string title, string id, string quality, string extension, MediaClip? clip)
    {
        var clipSuffix = clip is null ? string.Empty : $" [clip {clip.Start.Replace(':', '-')}-{clip.End.Replace(':', '-')}]";
        var baseName = $"{title} [{id}] [{quality}]{clipSuffix}";
        var path = Path.Combine(directory, $"{baseName}.{extension}");
        var index = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{baseName} ({index++}).{extension}");
        }

        return path;
    }

    private string MoveSubtitles(MediaEntry entry, MediaProbe probe, string? mediaDestination)
    {
        var subtitles = Directory.EnumerateFiles(entry.TempDirectory!)
            .Where(path => path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (subtitles.Count == 0)
        {
            throw new InvalidOperationException("Nie znaleziono napisów w wybranych językach.");
        }

        var destinationDirectory = mediaDestination is null
            ? Path.Combine(_outputRoot, "subtitles", "Online")
            : Path.GetDirectoryName(mediaDestination)!;
        Directory.CreateDirectory(destinationDirectory);
        var baseName = mediaDestination is null
            ? Path.Combine(destinationDirectory, $"{SanitizeFileName(probe.Title)} [{probe.Id}]")
            : Path.Combine(destinationDirectory, Path.GetFileNameWithoutExtension(mediaDestination));
        string? firstDestination = null;
        foreach (var subtitle in subtitles)
        {
            var language = Path.GetFileNameWithoutExtension(subtitle).Split('.').LastOrDefault();
            var destination = NextAvailableSubtitlePath(baseName, language ?? "sub");
            File.Move(subtitle, destination);
            firstDestination ??= destination;
        }

        return firstDestination!;
    }

    private static string NextAvailableSubtitlePath(string baseName, string language)
    {
        var path = $"{baseName}.{language}.srt";
        var index = 2;
        while (File.Exists(path))
        {
            path = $"{baseName}.{language} ({index++}).srt";
        }

        return path;
    }

    private static string? FindCompletedFile(string directory, string mediaId, string format)
    {
        var expected = Path.Combine(directory, $"{mediaId}.{format}");
        if (File.Exists(expected))
        {
            return expected;
        }

        return Directory.EnumerateFiles(directory)
            .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileNameWithoutExtension(path).Contains(".f", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private async Task EnsureVideoHasAudioAsync(string path, CancellationToken ct)
    {
        var ffprobePath = Path.Combine(Path.GetDirectoryName(_ffmpegPath) ?? "/usr/bin", "ffprobe");
        using var process = StartProcess(ffprobePath,
            ["-v", "error", "-show_entries", "stream=codec_type", "-of", "csv=p=0", path],
            Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());
        var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Nie można zweryfikować pliku MP4: {error.Trim()}");
        }

        var streams = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!streams.Contains("video", StringComparer.OrdinalIgnoreCase) || !streams.Contains("audio", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Wynikowy MP4 nie zawiera jednocześnie obrazu i dźwięku.");
        }
    }

    private static void TryKill(Process? process)
    {
        try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); } catch { }
    }

    private static void TryDelete(string? directory)
    {
        try { if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)) Directory.Delete(directory, true); } catch { }
    }

    private sealed record MediaProbe(string Id, string Title, double? DurationSeconds, bool IsLive);
    private sealed record ProcessOutput(string StandardOutput, string Error);
    private sealed record MediaClip(string Start, string End);
    private sealed record SubtitleOptions(string Languages, bool IncludeAuto);

    private sealed record MediaEntry
    {
        public MediaEntry(string id, string name, string url, string format, string quality, MediaClip? clip, SubtitleOptions? subtitles, string status,
            double progress, long sizeBytes, long downloadedBytes, string? outputPath, CancellationTokenSource cancellation)
        {
            Id = id;
            Name = name;
            Url = url;
            Format = format;
            Quality = quality;
            Clip = clip;
            Subtitles = subtitles;
            Status = status;
            Progress = progress;
            SizeBytes = sizeBytes;
            DownloadedBytes = downloadedBytes;
            OutputPath = outputPath;
            Cancellation = cancellation;
        }

        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public string Format { get; init; } = string.Empty;
        public string Quality { get; init; } = string.Empty;
        public MediaClip? Clip { get; init; }
        public SubtitleOptions? Subtitles { get; init; }
        public string Status { get; set; }
        public double Progress { get; set; }
        public long SizeBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public string? Error { get; set; }
        public string? OutputPath { get; set; }
        public CancellationTokenSource Cancellation { get; }
        public Process? Process { get; set; }
        public string? TempDirectory { get; set; }
    }
}
