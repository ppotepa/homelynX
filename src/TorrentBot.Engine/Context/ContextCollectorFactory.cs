using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Engine.Context;

public static class ContextCollectorFactory
{
    public static IContextCollector? Create(ISnapshotSource source)
    {
        // Default to Generic for any source (including new ones like torrent_search_results).
        // Special names kept for legacy naming in snapshots.
        return source.Name switch
        {
            "downloads" => new GenericContextCollector(source, "downloads"),
            "engine_jobs" => new GenericContextCollector(source, "jobs"),
            "media_files" => new GenericContextCollector(source, "media"),
            _ => new GenericContextCollector(source, source.Name)
        };
    }
}
