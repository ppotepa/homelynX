namespace TorrentBot.Plugins.Downloads;

public sealed class DownloaderRegistry
{
    private readonly Dictionary<string, IDownloader> _downloaders = new(StringComparer.OrdinalIgnoreCase);

    public DownloaderRegistry(IEnumerable<IDownloader> downloaders)
    {
        foreach (var downloader in downloaders)
        {
            _downloaders[downloader.Type] = downloader;
        }
    }

    public IReadOnlyList<IDownloader> GetAll() => _downloaders.Values.ToList();

    public IDownloader? Get(string type) =>
        _downloaders.TryGetValue(type, out var downloader) ? downloader : null;

    public IDownloader GetRequired(string type) =>
        Get(type) ?? throw new KeyNotFoundException($"Downloader '{type}' was not found.");
}