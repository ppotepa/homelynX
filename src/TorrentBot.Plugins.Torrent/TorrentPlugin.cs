using TorrentBot.Contracts.Plugins;
using TorrentBot.Engine.Context;
using TorrentBot.Plugins.Torrent.Capabilities;

namespace TorrentBot.Plugins.Torrent;

public sealed class TorrentPlugin : IPlugin
{
    public string Name => "torrent";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        var conversationStore = context.GetService<ConversationContextStore>()
            ?? throw new InvalidOperationException("ConversationContextStore is required for torrent search state.");
        var searchService = new TorrentSearchSnapshotService(conversationStore);
        context.RegisterService(searchService);
        context.RegisterSnapshotSource(searchService);

        context.RegisterCapability(TorrentContracts.Search, new TorrentSearchHandler(), "/search");
        context.RegisterCapability(TorrentContracts.List, new TorrentListHandler(), "/torrents");
        context.RegisterCapability(TorrentContracts.Pause, new TorrentPauseHandler(), "/torrent_pause");
        context.RegisterCapability(TorrentContracts.Resume, new TorrentResumeHandler(), "/torrent_resume");
        context.RegisterCapability(TorrentContracts.Delete, new TorrentDeleteHandler(), "/torrent_delete");
        context.RegisterCapability(TorrentContracts.MoreResults, new TorrentMoreResultsHandler(), "/more");
        context.RegisterCapability(TorrentContracts.SelectResult, new TorrentSelectResultHandler(), "/select");
        context.RegisterCapability(TorrentContracts.CancelSearch, new TorrentCancelSearchHandler(), "/cancel_search");
        context.RegisterCapability(TorrentContracts.DownloadCandidate, new TorrentDownloadCandidateHandler(), "/download_candidate");
    }
}