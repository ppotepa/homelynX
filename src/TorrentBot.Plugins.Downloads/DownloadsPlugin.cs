using TorrentBot.Contracts.ProcessManagers;
using TorrentBot.Contracts.Plugins;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Plugins.Downloads.Capabilities;
using TorrentBot.Plugins.Downloads.Downloaders;
using TorrentBot.Plugins.Downloads.ProcessManagers;
using TorrentBot.Plugins.Downloads.Snapshots;

namespace TorrentBot.Plugins.Downloads;

public sealed class DownloadsPlugin : IPlugin
{
    private readonly IJackettClient _jackett;
    private readonly IQBittorrentClient _qBittorrent;

    public DownloadsPlugin(IJackettClient? jackett = null, IQBittorrentClient? qBittorrent = null)
    {
        _jackett = jackett ?? new FakeJackettClient();
        _qBittorrent = qBittorrent ?? new FakeQBittorrentClient();
    }

    public string Name => "downloads";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        var jackett = _jackett;
        var qBittorrent = _qBittorrent;
        var torrentDownloader = new TorrentDownloader(jackett, qBittorrent);
        var urlDownloader = new UrlDownloader();
        var registry = new DownloaderRegistry([torrentDownloader, urlDownloader]);
        var processManager = new DownloadProcessManager(registry);

        context.RegisterService<IJackettClient>(jackett);
        context.RegisterService<IQBittorrentClient>(qBittorrent);
        context.RegisterService<DownloaderRegistry>(registry);
        context.RegisterService<IDownloadProcessManager>(processManager);
        context.RegisterService(torrentDownloader);
        context.RegisterService(urlDownloader);

        context.RegisterCapability(DownloadCapabilities.ListMetadata, new DownloadListHandler());
        context.RegisterCapability(DownloadCapabilities.SearchMetadata, new DownloadSearchHandler());
        context.RegisterCapability(DownloadCapabilities.StartMetadata, new DownloadStartHandler());
        context.RegisterCapability(DownloadCapabilities.PauseMetadata, new DownloadPauseHandler());
        context.RegisterCapability(DownloadCapabilities.ResumeMetadata, new DownloadResumeHandler());
        context.RegisterCapability(DownloadCapabilities.CancelMetadata, new DownloadCancelHandler());
        context.RegisterCapability(DownloadCapabilities.StartUrlMetadata, new DownloadStartUrlHandler());

        context.RegisterSnapshotSource(new DownloadsSnapshotSource(qBittorrent, urlDownloader, processManager));
        context.RegisterSnapshotSource(new JobsSnapshotSource(processManager));
    }
}