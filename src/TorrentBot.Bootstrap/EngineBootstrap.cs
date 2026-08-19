using TorrentBot.Acl;
using TorrentBot.Engine;
using TorrentBot.Contracts.Audit;
using TorrentBot.Engine.Audit;
using TorrentBot.Engine.Confirmations;
using TorrentBot.Engine.Context;
using TorrentBot.Engine.Jobs;
using TorrentBot.Engine.Migration;
using TorrentBot.Engine.Notifications;
using TorrentBot.Integrations.Clients;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Integrations.Models;
using TorrentBot.Plugins.Downloads;
using TorrentBot.Plugins.System;
using TorrentBot.Plugins.Torrent;

namespace TorrentBot.Bootstrap;

public static class EngineBootstrap
{
    public static EngineHost Create(
        Action<EngineHost>? configure = null,
        AclService? aclService = null,
        DownloadsPlugin? downloadsPlugin = null,
        IConfirmationStore? confirmationStore = null,
        IAuditSink? auditSink = null,
        object? botControlPlugin = null,
        object? mediaPlugin = null,
        object? jobsPlugin = null,
        ILegacyPythonDelegator? legacyDelegator = null,
        IDownloadCompletionNotifier? completionNotifier = null)
    {
        var audit = auditSink ?? CreateAuditSink();
        var engine = new EngineHost(new EngineOptions
        {
            AclService = aclService ?? AclService.FromEnvironment(),
            AuditSink = audit,
            ConfirmationStore = confirmationStore ?? new ConfirmationStore(),
            FeatureFlags = FeatureFlags.FromEnvironment(),
            LegacyDelegator = legacyDelegator ?? CreateLegacyDelegator(),
            JobRunner = new BackgroundJobRunner(),
            CompletionNotifier = completionNotifier
        });

        engine.RegisterPlugin(new SystemPlugin());
        engine.RegisterPlugin(downloadsPlugin ?? CreateDefaultDownloadsPlugin());
        engine.RegisterPlugin(new TorrentPlugin());
        
        configure?.Invoke(engine);
        return engine;
    }

    public static LegacyPythonCoexistence CreateCoexistenceRouter() => new();

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

    private static ILegacyPythonDelegator CreateLegacyDelegator()
    {
        var url = Environment.GetEnvironmentVariable("TORRENTBOT_LEGACY_PYTHON_URL");
        return !string.IsNullOrWhiteSpace(url)
            ? new HttpLegacyPythonDelegator(new HttpClient(), url)
            : new NoOpLegacyPythonDelegator();
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
