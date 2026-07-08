using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Engine.Context;

public sealed class GenericContextCollector : IContextCollector
{
    private readonly ISnapshotSource _snapshotSource;

    public string SourceName { get; }

    public GenericContextCollector(ISnapshotSource snapshotSource, string sourceName)
    {
        _snapshotSource = snapshotSource;
        SourceName = sourceName;
    }

    public async Task<ContextSnapshot> CollectAsync(CancellationToken ct = default)
    {
        var data = await _snapshotSource.GetSnapshotAsync(ct).ConfigureAwait(false);

        var state = new Dictionary<string, object?>
        {
            ["total_count"] = 0,
            ["items"] = new List<Dictionary<string, object?>>()
        };

        if (data is System.Collections.IEnumerable enumerable)
        {
            var totalCount = 0;
            var itemList = new List<Dictionary<string, object?>>();

            foreach (var item in enumerable)
            {
                if (item is Dictionary<string, object?> itemDict)
                {
                    totalCount++;
                    // Extract common fields
                    var summary = new Dictionary<string, object?>();
                    foreach (var (key, value) in itemDict)
                    {
                        // Only include scalar values, skip large nested objects
                        if (value is null || value is string || value is ValueType)
                        {
                            summary[key] = value;
                        }
                    }
                    itemList.Add(summary);
                }
            }

            state["total_count"] = totalCount;
            state["items"] = itemList.Take(10).ToList(); // Limit to 10 items for context
        }

        return new ContextSnapshot(state, DateTime.UtcNow);
    }
}
