namespace TorrentBot.Integrations.Interfaces;

public sealed record MediaLibraryItem(string Path, string Title, string Type, long SizeBytes, DateTimeOffset AddedAt);

public interface IMediaLibraryClient
{
    Task<IReadOnlyList<MediaLibraryItem>> ListItemsAsync(CancellationToken ct = default);
}