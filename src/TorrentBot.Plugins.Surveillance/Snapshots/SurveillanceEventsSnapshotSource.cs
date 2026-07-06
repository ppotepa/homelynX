using TorrentBot.Contracts.Repositories;
using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Plugins.Surveillance.Snapshots;

public sealed class SurveillanceEventsSnapshotSource : ISnapshotSource
{
    private readonly ISurveillanceClient _client;

    public SurveillanceEventsSnapshotSource(ISurveillanceClient client) => _client = client;

    public string Name => "surveillance_events";

    public QuerySourceMeta GetManifest() => new(
        Name,
        "Recent surveillance events from the recorder API",
        [
            new QueryFieldMeta("id", "string"),
            new QueryFieldMeta("type", "string"),
            new QueryFieldMeta("summary", "string"),
            new QueryFieldMeta("severity", "string"),
            new QueryFieldMeta("occurred_at", "string")
        ],
        LlmUsage: "Use for surveillance event questions");

    public async Task<object> GetSnapshotAsync(CancellationToken ct = default)
    {
        var events = await _client.GetEventsAsync(hours: "24h", ct: ct).ConfigureAwait(false);
        return events.Select(e => new Dictionary<string, object?>
        {
            ["id"] = e.Id,
            ["type"] = e.Type,
            ["summary"] = e.Summary,
            ["severity"] = e.Severity,
            ["occurred_at"] = e.OccurredAt.ToString("O")
        }).ToList();
    }
}