using TorrentBot.Contracts.Plugins;
using TorrentBot.Plugins.Torrent.Capabilities;

namespace TorrentBot.Plugins.Torrent;

public sealed class TorrentPlugin : IPlugin
{
    public string Name => "torrent";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        var sessionStore = new TorrentSearchSessionStore();
        context.RegisterService(sessionStore);

        context.RegisterCapability(TorrentCapabilities.SearchMetadata, new TorrentSearchHandler());
        context.RegisterCapability(TorrentCapabilities.ListMetadata, new TorrentListHandler());
        context.RegisterCapability(TorrentCapabilities.PauseMetadata, new TorrentPauseHandler());
        context.RegisterCapability(TorrentCapabilities.ResumeMetadata, new TorrentResumeHandler());
        context.RegisterCapability(TorrentCapabilities.DeleteMetadata, new TorrentDeleteHandler());
        context.RegisterCapability(TorrentCapabilities.MoreResultsMetadata, new TorrentMoreResultsHandler());
        context.RegisterCapability(TorrentCapabilities.SelectResultMetadata, new TorrentSelectResultHandler());
        context.RegisterCapability(TorrentCapabilities.CancelSearchMetadata, new TorrentCancelSearchHandler());
        context.RegisterCapability(TorrentCapabilities.DownloadCandidateMetadata, new TorrentDownloadCandidateHandler());
    }
}