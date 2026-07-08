namespace TorrentBot.Contracts.Context;

public interface IContextCollector
{
    Task<ContextSnapshot> CollectAsync(CancellationToken ct = default);
    string SourceName { get; }
}
