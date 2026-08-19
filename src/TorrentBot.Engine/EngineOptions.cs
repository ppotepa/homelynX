using Microsoft.Extensions.Logging;
using TorrentBot.Acl;
using TorrentBot.Contracts.Audit;
using TorrentBot.Engine.Confirmations;
using TorrentBot.Engine.Context;
using TorrentBot.Engine.Jobs;
using TorrentBot.Engine.Notifications;

namespace TorrentBot.Engine;

public sealed class EngineOptions
{
    public ILoggerFactory? LoggerFactory { get; init; }
    public bool DryRunSkipsJobPersistence { get; init; } = true;
    public AclService? AclService { get; init; }
    public IConfirmationStore? ConfirmationStore { get; init; }
    public IAuditSink? AuditSink { get; init; }
    public IJobRunner? JobRunner { get; init; }
    public IDownloadCompletionNotifier? CompletionNotifier { get; init; }
    public ConversationContextStore? ConversationContextStore { get; set; }
}
