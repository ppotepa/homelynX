using Microsoft.Extensions.Logging;
using TorrentBot.Acl;
using TorrentBot.Contracts.Audit;
using TorrentBot.Engine.Confirmations;
using TorrentBot.Engine.Jobs;
using TorrentBot.Engine.Migration;
using TorrentBot.Engine.Notifications;
using TorrentBot.Llm;

namespace TorrentBot.Engine;

public sealed class EngineOptions
{
    public ILoggerFactory? LoggerFactory { get; init; }
    public bool DryRunSkipsJobPersistence { get; init; } = true;
    public AclService? AclService { get; init; }
    public LlmPipeline? LlmPipeline { get; init; }
    public ConfirmationStore? ConfirmationStore { get; init; }
    public IAuditSink? AuditSink { get; init; }
    public FeatureFlags FeatureFlags { get; init; } = new();
    public ILegacyPythonDelegator? LegacyDelegator { get; init; }
    public IJobRunner? JobRunner { get; init; }
    public IDownloadCompletionNotifier? CompletionNotifier { get; init; }
}