using TorrentBot.Contracts.Query;
using TorrentBot.Contracts.Repositories;
using TorrentBot.Query;

namespace TorrentBot.Engine.Repositories;

public sealed class RepositoryAggregator
{
    private readonly Dictionary<string, ISnapshotSource> _sources = new(StringComparer.Ordinal);
    private readonly DuckDbQueryEngine _queryEngine = new();
    private bool _frozen;

    public void Register(ISnapshotSource source)
    {
        if (_frozen)
        {
            throw new InvalidOperationException("Repository aggregator is frozen.");
        }

        ArgumentNullException.ThrowIfNull(source);

        if (_sources.ContainsKey(source.Name))
        {
            throw new InvalidOperationException($"Snapshot source '{source.Name}' is already registered.");
        }

        _sources[source.Name] = source;
    }

    public void Freeze() => _frozen = true;

    public IReadOnlyList<QuerySourceMeta> GetManifests() =>
        _sources.Values.Select(s => s.GetManifest()).OrderBy(m => m.Name).ToList();

    public ISnapshotSource? GetSource(string name) =>
        _sources.TryGetValue(name, out var snapshotSource) ? snapshotSource : null;

    public async Task<QueryResult> QueryAsync(string source, QuerySpec spec, CancellationToken ct = default)
    {
        if (!_sources.TryGetValue(source, out var snapshotSource))
        {
            throw new KeyNotFoundException($"Snapshot source '{source}' was not found.");
        }

        var snapshot = await snapshotSource.GetSnapshotAsync(ct).ConfigureAwait(false);
        var rows = snapshot switch
        {
            IEnumerable<Dictionary<string, object?>> dictionaries => dictionaries.ToList(),
            IEnumerable<object> objects => objects
                .Select(o => o.GetType().GetProperties()
                    .ToDictionary(p => p.Name, p => (object?)p.GetValue(o)))
                .ToList(),
            _ => []
        };

        return _queryEngine.Execute(snapshotSource.GetManifest(), rows, spec);
    }

    public Task<QueryResult> QueryAsync(string source, IReadOnlyDictionary<string, object?> raw, CancellationToken ct = default)
    {
        if (!_sources.TryGetValue(source, out var snapshotSource))
        {
            throw new KeyNotFoundException($"Snapshot source '{source}' was not found.");
        }

        return QueryAsyncInternal(snapshotSource, raw, ct);
    }

    private async Task<QueryResult> QueryAsyncInternal(ISnapshotSource snapshotSource, IReadOnlyDictionary<string, object?> raw, CancellationToken ct)
    {
        var snapshot = await snapshotSource.GetSnapshotAsync(ct).ConfigureAwait(false);
        var rows = snapshot switch
        {
            IEnumerable<Dictionary<string, object?>> dictionaries => dictionaries.ToList(),
            _ => []
        };

        return _queryEngine.Execute(snapshotSource.GetManifest(), rows, raw);
    }
}