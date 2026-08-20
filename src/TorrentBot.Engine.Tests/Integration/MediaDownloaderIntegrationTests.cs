using System.Diagnostics;
using TorrentBot.Plugins.Downloads;
using TorrentBot.Plugins.Downloads.Downloaders;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class MediaDownloaderIntegrationTests
{
    [Fact]
    public async Task Mp4_without_subtitles_reaches_completed_state()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"homelynx-media-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var fakeYtDlp = Path.Combine(root, "yt-dlp");
            await File.WriteAllTextAsync(fakeYtDlp, """
                #!/bin/sh
                set -eu
                if printf '%s\n' "$@" | grep -q -- '--dump-single-json'; then
                  printf '%s\n' '{"id":"fixture","title":"fixture","duration":1,"is_live":false}'
                  exit 0
                fi
                output=""
                previous=""
                for argument in "$@"; do
                  if [ "$previous" = "-o" ]; then output="$argument"; fi
                  previous="$argument"
                done
                output=$(printf '%s' "$output" | sed 's/%(ext)s$/mp4/')
                /usr/bin/ffmpeg -loglevel error -f lavfi -i color=c=black:s=16x16:d=0.2 \
                  -f lavfi -i anullsrc=r=8000:cl=mono -shortest -c:v libx264 -c:a aac "$output"
                """);
            File.SetUnixFileMode(fakeYtDlp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var library = Path.Combine(root, "library");
            var temp = Path.Combine(root, "temp");
            var downloader = new MediaDownloader(
                ytDlpPath: fakeYtDlp,
                ffmpegPath: "/usr/bin/ffmpeg",
                outputRoot: library,
                tempRoot: temp,
                maxDurationSeconds: 60,
                maxConcurrency: 1,
                maxPerUser: 1,
                timeoutSeconds: 30,
                enabled: true);

            var ticket = await downloader.StartAsync(new DownloadStartRequest(
                Provider: "media",
                Url: "https://www.youtube.com/watch?v=fixture",
                MediaFormat: "mp4",
                MediaQuality: "360",
                OwnerUserId: "integration-test"));

            DownloadStatus? status = null;
            for (var attempt = 0; attempt < 50; attempt++)
            {
                await Task.Delay(100);
                status = await downloader.GetStatusAsync(ticket.DownloadId);
                if (status.Status is "completed" or "failed" or "cancelled")
                {
                    break;
                }
            }

            Assert.NotNull(status);
            Assert.Equal("completed", status!.Status);
            Assert.Equal(1, status.Progress);
            Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(library, "movies", "Online"), "*.mp4"));
            Assert.Empty(Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
