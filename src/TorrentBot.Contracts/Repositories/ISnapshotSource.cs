namespace TorrentBot.Contracts.Repositories;

public interface ISnapshotSource
{
    string Name { get; }
    QuerySourceMeta GetManifest();
    Task<object> GetSnapshotAsync(CancellationToken ct = default);
}