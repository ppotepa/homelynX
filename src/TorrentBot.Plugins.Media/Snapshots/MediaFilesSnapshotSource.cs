using TorrentBot.Contracts.Repositories;
using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Plugins.Media.Snapshots;

public sealed class MediaFilesSnapshotSource : ISnapshotSource
{
    private readonly IMediaLibraryClient _client;

    public MediaFilesSnapshotSource(IMediaLibraryClient client) => _client = client;

    public string Name => "media_files";

    public QuerySourceMeta GetManifest() => new(
        Name: Name,
        Description: "Indexed media library files",
        Fields:
        [
            new QueryFieldMeta("path", "string"),
            new QueryFieldMeta("title", "string"),
            new QueryFieldMeta("type", "string"),
            new QueryFieldMeta("size_bytes", "number"),
            new QueryFieldMeta("added_at", "string")
        ],
        LlmUsage: "Use to answer questions about existing media in the library",
        ExampleQueries:
        [
            "{ \"source\": \"media_files\", \"where\": [{ \"field\": \"type\", \"op\": \"=\", \"value\": \"movie\" }] }"
        ]);

    public async Task<object> GetSnapshotAsync(CancellationToken ct = default)
    {
        var items = await _client.ListItemsAsync(ct).ConfigureAwait(false);
        return items.Select(item => new Dictionary<string, object?>
        {
            ["path"] = item.Path,
            ["title"] = item.Title,
            ["type"] = item.Type,
            ["size_bytes"] = item.SizeBytes,
            ["added_at"] = item.AddedAt.ToString("O")
        }).ToList();
    }
}