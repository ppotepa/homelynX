using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Integrations.Clients;

public sealed class FilesystemMediaLibraryClient : IMediaLibraryClient
{
    private readonly string _root;

    public FilesystemMediaLibraryClient(string root)
    {
        _root = root;
    }

    public Task<IReadOnlyList<MediaLibraryItem>> ListItemsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_root))
        {
            return Task.FromResult<IReadOnlyList<MediaLibraryItem>>([]);
        }

        var items = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Take(200)
            .Select(path => new MediaLibraryItem(
                path,
                Path.GetFileNameWithoutExtension(path),
                InferType(path),
                new FileInfo(path).Length,
                File.GetCreationTimeUtc(path)))
            .ToList();

        return Task.FromResult<IReadOnlyList<MediaLibraryItem>>(items);
    }

    private static string InferType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".mkv" or ".mp4" or ".avi" => "video",
            ".mp3" or ".flac" => "music",
            _ => "file"
        };
    }
}