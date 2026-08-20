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
        var mediaDownloader = new MediaDownloader();
        var registry = new DownloaderRegistry([torrentDownloader, mediaDownloader]);
        var processManager = new DownloadProcessManager(registry);

        context.RegisterService<IJackettClient>(jackett);
        context.RegisterService<IQBittorrentClient>(qBittorrent);
        context.RegisterService<DownloaderRegistry>(registry);
        context.RegisterService<IDownloadProcessManager>(processManager);
        context.RegisterService(torrentDownloader);
        context.RegisterService(mediaDownloader);

        context.RegisterCapability(DownloadContracts.List, new DownloadListHandler(), "/downloads");
        context.RegisterCapability(DownloadContracts.Search, new DownloadSearchHandler(), "/download_search");
        context.RegisterCapability(DownloadContracts.Start, new DownloadStartHandler(), "/download");
        context.RegisterCapability(DownloadContracts.StartMedia, new DownloadStartHandler(), "/download_media");
        context.RegisterCapability(DownloadContracts.Pause, new DownloadPauseHandler(), "/pause");
        context.RegisterCapability(DownloadContracts.Resume, new DownloadResumeHandler(), "/resume");
        context.RegisterCapability(DownloadContracts.Cancel, new DownloadCancelHandler(), "/cancel");
        context.RegisterSnapshotSource(new DownloadsSnapshotSource(qBittorrent, processManager, mediaDownloader));
        context.RegisterSnapshotSource(new JobsSnapshotSource(processManager));
    }
}
