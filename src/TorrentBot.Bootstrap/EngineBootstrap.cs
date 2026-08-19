using TorrentBot.Acl;
using TorrentBot.Engine;
using TorrentBot.Contracts.Audit;
using TorrentBot.Engine.Audit;
using TorrentBot.Engine.Confirmations;
using TorrentBot.Engine.Context;
using TorrentBot.Engine.Jobs;
using TorrentBot.Engine.Notifications;
using TorrentBot.Integrations.Clients;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Integrations.Models;
using TorrentBot.Plugins.BotControl;
using TorrentBot.Plugins.Downloads;
using TorrentBot.Plugins.Jobs;
using TorrentBot.Plugins.Media;
using TorrentBot.Plugins.Query;
using TorrentBot.Plugins.System;
using TorrentBot.Plugins.Torrent;
using TorrentBot.Plugins.Tools;

namespace TorrentBot.Bootstrap;

public static class EngineBootstrap
{
    public static EngineHost Create(
        Action<EngineHost>? configure = null,
        AclService? aclService = null,
        DownloadsPlugin? downloadsPlugin = null,
        IConfirmationStore? confirmationStore = null,
        IAuditSink? auditSink = null,
        BotControlPlugin? botControlPlugin = null,
        MediaPlugin? mediaPlugin = null,
        JobsPlugin? jobsPlugin = null,
        IDownloadCompletionNotifier? completionNotifier = null)
    {
        var audit = auditSink ?? CreateAuditSink();
        var engine = new EngineHost(new EngineOptions
        {
            AclService = aclService ?? AclService.FromEnvironment(),
            AuditSink = audit,
            ConfirmationStore = confirmationStore ?? new ConfirmationStore(),
            JobRunner = new BackgroundJobRunner(),
            CompletionNotifier = completionNotifier
        });

        engine.RegisterPlugin(new SystemPlugin());
        engine.RegisterPlugin(new QueryPlugin());
        engine.RegisterPlugin(downloadsPlugin ?? CreateDefaultDownloadsPlugin());
        engine.RegisterPlugin(new TorrentPlugin());
        engine.RegisterPlugin(mediaPlugin ?? CreateDefaultMediaPlugin());
        engine.RegisterPlugin(botControlPlugin ?? new BotControlPlugin());
        engine.RegisterPlugin(jobsPlugin ?? new JobsPlugin());
        engine.RegisterPlugin(new ToolsPlugin());
        
        configure?.Invoke(engine);
        return engine;
    }

    public static (IJackettClient Jackett, IQBittorrentClient QBittorrent) CreateTorrentClients()
    {
        var jackettUrl = HomelynxEnv.FirstNonEmpty(
            Environment.GetEnvironmentVariable("JACKETT_URL"),
            Environment.GetEnvironmentVariable("TORRENTBOT_JACKETT_URL"),
            HomelynxEnv.GetServiceUrl(null, "JACKETT_HOST", "JACKETT_PORT", "JACKETT_HTTPS"));
        var jackettKey = HomelynxEnv.FirstNonEmpty(
            Environment.GetEnvironmentVariable("JACKETT_API_KEY"),
            Environment.GetEnvironmentVariable("TORRENTBOT_JACKETT_API_KEY"));
        var qbitUrl = HomelynxEnv.FirstNonEmpty(
            Environment.GetEnvironmentVariable("QBITTORRENT_URL"),
            Environment.GetEnvironmentVariable("TORRENTBOT_QBITTORRENT_URL"),
            HomelynxEnv.GetServiceUrl(null, "QBIT_HOST", "QBIT_PORT", "QBIT_HTTPS"));
        var qbitUser = HomelynxEnv.FirstNonEmpty(
            Environment.GetEnvironmentVariable("QBITTORRENT_USER"),
            Environment.GetEnvironmentVariable("QBIT_USERNAME"),
            Environment.GetEnvironmentVariable("TORRENTBOT_QBITTORRENT_USER"));
        var qbitPass = HomelynxEnv.FirstNonEmpty(
            Environment.GetEnvironmentVariable("QBITTORRENT_PASS"),
            Environment.GetEnvironmentVariable("QBIT_PASSWORD"),
            Environment.GetEnvironmentVariable("TORRENTBOT_QBITTORRENT_PASS"));

        var jackettIndexers = Environment.GetEnvironmentVariable("JACKETT_SEARCH_INDEXERS");

        IJackettClient jackett = !string.IsNullOrWhiteSpace(jackettUrl)
            ? new JackettClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, jackettUrl, jackettKey, jackettIndexers)
            : CreateFakeJackett();

        IQBittorrentClient qbit = !string.IsNullOrWhiteSpace(qbitUrl)
            ? new QBittorrentClient(new HttpClient(), qbitUrl, qbitUser, qbitPass)
            : CreateFakeQBittorrent();

        return (jackett, qbit);
    }

    private static IAuditSink CreateAuditSink()
    {
        var sqlitePath = Environment.GetEnvironmentVariable("TORRENTBOT_AUDIT_DB");
        if (string.IsNullOrWhiteSpace(sqlitePath))
        {
            return new InMemoryAuditSink();
        }

        var directory = Path.GetDirectoryName(sqlitePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new PortalAuditSink($"Data Source={sqlitePath}");
    }

    private static MediaPlugin CreateDefaultMediaPlugin()
    {
        var ttsUrl = HomelynxEnv.FirstNonEmpty(
            Environment.GetEnvironmentVariable("TORRENTBOT_TTS_URL"),
            HomelynxEnv.GetServiceUrl(null, "TTS_HOST", "TTS_PORT", "TTS_HTTPS"));
        ITtsClient? tts = !string.IsNullOrWhiteSpace(ttsUrl) ? new HttpTtsClient(new HttpClient(), ttsUrl) : null;
        return new MediaPlugin(tts);
    }

    private static DownloadsPlugin CreateDefaultDownloadsPlugin()
    {
        var (jackett, qbit) = CreateTorrentClients();
        return new DownloadsPlugin(jackett, qbit);
    }

    private static FakeJackettClient CreateFakeJackett()
    {
        var jackett = new FakeJackettClient();
        jackett.SetResults(
        [
            new TorrentSearchResult("seed-ubuntu", "Ubuntu 24.04 LTS", "magnet:?xt=urn:btih:seedubuntu", 4_000_000_000, 120, "jackett")
        ]);
        return jackett;
    }

    private static FakeQBittorrentClient CreateFakeQBittorrent()
    {
        var qBittorrent = new FakeQBittorrentClient();
        qBittorrent.AddTorrentAsync(
            new AddTorrentRequest("magnet:?xt=urn:btih:seedubuntu&dn=Ubuntu+24.04+LTS"))
            .GetAwaiter()
            .GetResult();
        return qBittorrent;
    }
}
