using TorrentBot.Acl;
using TorrentBot.Engine;
using TorrentBot.Contracts.Audit;
using TorrentBot.Engine.Audit;
using TorrentBot.Engine.Confirmations;
using TorrentBot.Engine.Jobs;
using TorrentBot.Engine.Migration;
using TorrentBot.Engine.Notifications;
using TorrentBot.Integrations.Clients;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Integrations.Models;
using TorrentBot.Llm;
using TorrentBot.Plugins.BotControl;
using TorrentBot.Plugins.Downloads;
using TorrentBot.Plugins.Jobs;
using TorrentBot.Plugins.Media;
using TorrentBot.Plugins.Query;
using TorrentBot.Plugins.Surveillance;
using TorrentBot.Plugins.System;
using TorrentBot.Plugins.Torrent;

namespace TorrentBot.Bootstrap;

public static class EngineBootstrap
{
    public static EngineHost Create(
        Action<EngineHost>? configure = null,
        AclService? aclService = null,
        DownloadsPlugin? downloadsPlugin = null,
        ConfirmationStore? confirmationStore = null,
        LlmPipeline? llmPipeline = null,
        IAuditSink? auditSink = null,
        SurveillancePlugin? surveillancePlugin = null,
        BotControlPlugin? botControlPlugin = null,
        MediaPlugin? mediaPlugin = null,
        JobsPlugin? jobsPlugin = null,
        ILegacyPythonDelegator? legacyDelegator = null,
        IDownloadCompletionNotifier? completionNotifier = null)
    {
        var audit = auditSink ?? CreateAuditSink();
        var pipeline = llmPipeline ?? CreateLlmPipeline(audit);
        var engine = new EngineHost(new EngineOptions
        {
            AclService = aclService ?? AclService.FromEnvironment(),
            AuditSink = audit,
            ConfirmationStore = confirmationStore ?? new ConfirmationStore(),
            LlmPipeline = pipeline,
            FeatureFlags = FeatureFlags.FromEnvironment(),
            LegacyDelegator = legacyDelegator ?? CreateLegacyDelegator(),
            JobRunner = new BackgroundJobRunner(),
            CompletionNotifier = completionNotifier
        });

        engine.RegisterPlugin(new SystemPlugin());
        engine.RegisterPlugin(new QueryPlugin());
        engine.RegisterPlugin(downloadsPlugin ?? CreateDefaultDownloadsPlugin());
        engine.RegisterPlugin(new TorrentPlugin());
        engine.RegisterPlugin(mediaPlugin ?? CreateDefaultMediaPlugin());
        engine.RegisterPlugin(surveillancePlugin ?? CreateDefaultSurveillancePlugin());
        engine.RegisterPlugin(botControlPlugin ?? new BotControlPlugin());
        engine.RegisterPlugin(jobsPlugin ?? new JobsPlugin());
        configure?.Invoke(engine);
        return engine;
    }

    public static LlmPipeline CreateLlmPipeline(IAuditSink? auditSink = null)
    {
        var ollamaUrl = HomelynxEnv.FirstNonEmpty(
            Environment.GetEnvironmentVariable("TORRENTBOT_OLLAMA_URL"),
            Environment.GetEnvironmentVariable("OLLAMA_HOST"),
            HomelynxEnv.GetServiceUrl(null, "LLM_HOST", "LLM_PORT", "LLM_HTTPS"));
        if (!string.IsNullOrWhiteSpace(ollamaUrl))
        {
            var defaultModel = HomelynxEnv.FirstNonEmpty(
                Environment.GetEnvironmentVariable("LLM_MODEL"),
                "llama3")!;
            var plannerModel = HomelynxEnv.FirstNonEmpty(
                Environment.GetEnvironmentVariable("TORRENTBOT_OLLAMA_PLANNER_MODEL"),
                Environment.GetEnvironmentVariable("LLM_PLANNER_MODEL"),
                defaultModel)!;
            var executorModel = HomelynxEnv.FirstNonEmpty(
                Environment.GetEnvironmentVariable("TORRENTBOT_OLLAMA_EXECUTOR_MODEL"),
                plannerModel)!;
            var responderModel = HomelynxEnv.FirstNonEmpty(
                Environment.GetEnvironmentVariable("TORRENTBOT_OLLAMA_RESPONDER_MODEL"),
                Environment.GetEnvironmentVariable("LLM_RESPONDER_MODEL"),
                plannerModel)!;
            var plannerClient = new OllamaLlmClient(new HttpClient(), ollamaUrl, plannerModel);
            var executorClient = new OllamaLlmClient(new HttpClient(), ollamaUrl, executorModel);
            var responderClient = new OllamaLlmClient(new HttpClient(), ollamaUrl, responderModel);
            return new LlmPipeline(
                new OllamaLlmPlanner(plannerClient),
                new AuditingLlmExecutor(new OllamaLlmExecutor(executorClient), auditSink),
                new OllamaLlmResponder(responderClient),
                auditSink);
        }

        return new LlmPipeline(
            new UnconfiguredLlmPlanner(),
            new AuditingLlmExecutor(new StubLlmExecutor(), auditSink),
            auditSink: auditSink);
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

        IJackettClient jackett = !string.IsNullOrWhiteSpace(jackettUrl)
            ? new JackettClient(new HttpClient(), jackettUrl, jackettKey)
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

    private static SurveillancePlugin CreateDefaultSurveillancePlugin()
    {
        var baseUrl = HomelynxEnv.FirstNonEmpty(
            Environment.GetEnvironmentVariable("TORRENTBOT_SURVEILLANCE_URL"),
            HomelynxEnv.GetServiceUrl(null, "SURV_HOST", "SURV_PORT", "SURV_HTTPS"));
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return new SurveillancePlugin(new HttpSurveillanceClient(new HttpClient(), baseUrl));
        }

        return new SurveillancePlugin(new FakeSurveillanceClient());
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