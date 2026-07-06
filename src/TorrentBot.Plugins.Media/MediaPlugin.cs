using TorrentBot.Contracts.Plugins;
using TorrentBot.Integrations.Clients;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Plugins.Media.Capabilities;
using TorrentBot.Plugins.Media.Snapshots;

namespace TorrentBot.Plugins.Media;

public sealed class MediaPlugin : IPlugin
{
    private readonly ITtsClient _ttsClient;
    private readonly IMediaLibraryClient _mediaLibraryClient;

    public MediaPlugin(ITtsClient? ttsClient = null, IMediaLibraryClient? mediaLibraryClient = null)
    {
        _ttsClient = ttsClient ?? new FakeTtsClient();
        _mediaLibraryClient = mediaLibraryClient ?? CreateDefaultMediaLibraryClient();
    }

    public string Name => "media";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        context.RegisterService<ITtsClient>(_ttsClient);
        context.RegisterService<IMediaLibraryClient>(_mediaLibraryClient);
        context.RegisterCapability(MediaCapabilities.ListMetadata, new MediaListHandler());
        context.RegisterCapability(MediaCapabilities.TtsSayMetadata, new TtsSayHandler());
        context.RegisterSnapshotSource(new MediaFilesSnapshotSource(_mediaLibraryClient));
    }

    private static IMediaLibraryClient CreateDefaultMediaLibraryClient()
    {
        var mediaRoot = Environment.GetEnvironmentVariable("TORRENTBOT_MEDIA_ROOT");
        return !string.IsNullOrWhiteSpace(mediaRoot)
            ? new FilesystemMediaLibraryClient(mediaRoot)
            : new FixtureMediaLibraryClient();
    }

    private sealed class FixtureMediaLibraryClient : IMediaLibraryClient
    {
        public Task<IReadOnlyList<MediaLibraryItem>> ListItemsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MediaLibraryItem>>(
            [
                new("/media/movies/Inception (2010)/Inception.mkv", "Inception", "movie", 8_589_934_592L, DateTimeOffset.Parse("2026-01-15T10:00:00Z")),
                new("/media/series/Breaking Bad/S01E01.mkv", "Breaking Bad S01E01", "episode", 1_610_612_736L, DateTimeOffset.Parse("2026-02-02T18:30:00Z"))
            ]);
    }
}