using System.Collections.Concurrent;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Engine.Context;

public sealed class ConversationContextStore
{
    private readonly ConcurrentDictionary<string, ConversationContext> _contexts = new();
    private readonly List<IContextCollector> _collectors = [];

    public int CollectorCount => _collectors.Count;

    public void RegisterCollector(IContextCollector collector)
    {
        _collectors.Add(collector);
    }

    public ConversationContext GetOrCreate(string sessionId, string userId)
    {
        return _contexts.GetOrAdd(sessionId, _ => new ConversationContext(sessionId, userId));
    }

    public ConversationContext? Get(string sessionId)
    {
        return _contexts.TryGetValue(sessionId, out var ctx) ? ctx : null;
    }

    public IEnumerable<ConversationContext> GetAllForUser(string userId) =>
        _contexts.Values.Where(c => string.Equals(c.UserId, userId, StringComparison.Ordinal));

    public IReadOnlyList<ConversationContext> GetAllContexts() => _contexts.Values.ToList();

    public async Task RefreshSnapshotsAsync(ConversationContext context, CancellationToken ct = default)
    {
        foreach (var collector in _collectors)
        {
            try
            {
                var snapshot = await collector.CollectAsync(ct).ConfigureAwait(false);
                context.UpdateSnapshot(collector.SourceName, snapshot);
            }
            catch
            {
                // Collector failed, skip
            }
        }
    }

    public void Remove(string sessionId)
    {
        _contexts.TryRemove(sessionId, out _);
    }
}