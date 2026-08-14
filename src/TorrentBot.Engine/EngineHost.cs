using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TorrentBot.Acl;
using TorrentBot.Contracts.Bus;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Jobs;
using TorrentBot.Contracts.Plugins;
using TorrentBot.Contracts.Query;
using TorrentBot.Contracts.ProcessManagers;
using TorrentBot.Contracts.Repositories;
using TorrentBot.Contracts.Audit;
using TorrentBot.Engine.Notifications;
using TorrentBot.Engine.Audit;
using TorrentBot.Engine.Bus;
using TorrentBot.Engine.Capabilities;
using TorrentBot.Engine.Confirmations;
using TorrentBot.Engine.Context;
using TorrentBot.Engine.Jobs;
using TorrentBot.Engine.Plugins;
using TorrentBot.Engine.Repositories;

namespace TorrentBot.Engine;

public sealed class EngineHost : IEngine
{
    private readonly EngineOptions _options;
    private readonly List<IPlugin> _pendingPlugins = [];
    private readonly CapabilityRegistry _capabilities = new();
    private readonly RepositoryAggregator _repositories = new();
    private readonly Dictionary<Type, object> _services = new();
    private readonly PluginRegistrationContext _registrationContext;
    private readonly object _lifecycleGate = new();

    private IInternalBus? _bus;
    private IJobTracker? _jobTracker;
    private IJobRunner? _jobRunner;
    private DownloadJobMonitor? _downloadMonitor;
    private IDisposable? _completionSubscription;
    private ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private bool _isRunning;

    public EngineHost(EngineOptions? options = null)
    {
        _options = options ?? new EngineOptions();
        _registrationContext = new PluginRegistrationContext(_capabilities, _repositories);
    }

    public bool IsRunning
    {
        get { lock (_lifecycleGate) { return _isRunning; } }
    }

    public ConversationContextStore? ConversationContextStore => _options.ConversationContextStore;

    public IReadOnlyList<CapabilityContract> GetCapabilityContracts(string? scope = null)
    {
        var all = _capabilities.GetAllContracts();
        if (string.IsNullOrWhiteSpace(scope))
        {
            return all;
        }

        return all
            .Where(c => string.Equals(c.Scope, scope, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Scope, "all", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void SetConversationContextStore(ConversationContextStore store)
    {
        _options.ConversationContextStore = store;
    }

    public void RegisterPlugin(IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        lock (_lifecycleGate)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Cannot register plugins after the engine has started.");
            }

            _pendingPlugins.Add(plugin);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            if (_isRunning)
            {
                return Task.CompletedTask;
            }

            _loggerFactory = _options.LoggerFactory ?? NullLoggerFactory.Instance;
            _bus = CreateEventBus();
            _jobTracker = new InMemoryJobTracker();

            _options.ConversationContextStore ??= new ConversationContextStore();
            _registrationContext.RegisterService(_options.ConversationContextStore);

            foreach (var plugin in _pendingPlugins)
            {
                PluginLoader.RegisterPlugin(plugin, _registrationContext);
            }

            foreach (var (serviceType, service) in _registrationContext.GetRegisteredServices())
            {
                _services[serviceType] = service;
            }

            _capabilities.Freeze();
            _repositories.Freeze();

            foreach (var source in _repositories.GetAllSources())
            {
                var collector = ContextCollectorFactory.Create(source);
                if (collector is not null)
                {
                    _options.ConversationContextStore.RegisterCollector(collector);
                }
            }

            _jobRunner = _options.JobRunner;
            _jobRunner?.Start(_jobTracker, _bus, cancellationToken);

            _completionSubscription = _bus.Subscribe<DownloadCompletedEvent>(message =>
            {
                _options.CompletionNotifier?.Notify(message.Payload);
            });

            if (_services.TryGetValue(typeof(IDownloadProcessManager), out var processManager)
                && processManager is IDownloadProcessManager downloadManager)
            {
                _downloadMonitor = new DownloadJobMonitor(_jobTracker, downloadManager);
                _downloadMonitor.Start(cancellationToken);
            }

            _isRunning = true;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        IInternalBus? busToDispose;
        lock (_lifecycleGate)
        {
            if (!_isRunning)
            {
                return;
            }

            _completionSubscription?.Dispose();
            _completionSubscription = null;
            _downloadMonitor?.Dispose();
            _downloadMonitor = null;
            _jobRunner?.Stop();
            _jobRunner = null;
            _isRunning = false;
            busToDispose = _bus;
            _bus = null;
            _jobTracker = null;
        }

        if (busToDispose is IAsyncDisposable asyncBus)
        {
            await asyncBus.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<ExecutionResult> SubmitAsync(Invocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(invocation.RequestContext);
        ArgumentNullException.ThrowIfNull(invocation.User);

        if (!invocation.IsExplicit)
        {
            return new ExecutionResult(Success: false,
                Error: "Only slash commands are supported. Use /help to list available commands.");
        }

        var capabilityName = ResolveCapabilityInternal(invocation);
        if (capabilityName is null)
        {
            return new ExecutionResult(Success: false, Error: "Capability was not resolved.");
        }

        return await ExecuteCapabilityAsync(invocation, capabilityName, invocation.Parameters, cancellationToken)
            .ConfigureAwait(false);
    }

    public Job? GetJob(string jobId) => RequireJobTracker().Get(jobId);

    public IReadOnlyList<Job> ListJobs() => RequireJobTracker().GetAll().ToList();

    public IDisposable Subscribe<T>(Action<CorrelatedMessage<T>> handler) where T : class =>
        RequireBus().Subscribe(handler);

    public IReadOnlyList<QuerySourceMeta> GetQuerySourceManifests() => _repositories.GetManifests();

    public IReadOnlyList<TorrentBot.Contracts.Repositories.ISnapshotSource> GetSnapshotSources() => _repositories.GetAllSources();

    public string SeedCompletedJob(string type = "download.url", string? chatId = null, string? userId = "admin")
    {
        var tracker = RequireJobTracker();
        var context = new RequestContext(
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            userId ?? "admin",
            source: "test-seed",
            chatId: chatId);
        var jobId = tracker.Create(type, new { seeded = true }, new JobOptions(), context);
        tracker.Update(jobId, job => job with { Status = JobStatus.Succeeded, Progress = 1.0 });
        return jobId;
    }

    public Task<QueryResult> QueryAsync(string source, QuerySpec spec, CancellationToken ct = default) =>
        _repositories.QueryAsync(source, spec, ct);

    private async Task<ExecutionResult> ExecuteCapabilityAsync(
        Invocation invocation,
        string capabilityName,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        var registered = _capabilities.Get(capabilityName);
        if (registered is null)
        {
            return new ExecutionResult(Success: false, Error: $"Capability '{capabilityName}' was not found.");
        }

        if (_options.AclService is not null && !_options.AclService.Allows(invocation.User, registered.Metadata))
        {
            _options.AuditSink?.Write("acl_denied", invocation.RequestContext, capabilityName, false);
            return new ExecutionResult(Success: false, Error: $"Access denied for capability '{capabilityName}'.");
        }

        var confirmationError = CheckConfirmation(registered.Metadata, invocation, parameters);
        if (confirmationError is not null)
        {
            return confirmationError;
        }

        var bus = RequireBus();
        var jobTracker = RequireJobTracker();
        var requestContext = new RequestContext(
            invocation.RequestContext.TraceId,
            invocation.RequestContext.InvocationId,
            invocation.RequestContext.UserId,
            invocation.RequestContext.JobId,
            capabilityName,
            invocation.RequestContext.Source,
            invocation.RequestContext.ChatId,
            invocation.RequestContext.MessageId,
            invocation.RequestContext.Properties);

        var engineContext = new EngineContext(
            bus,
            jobTracker,
            _capabilities,
            _repositories,
            requestContext,
            invocation.User,
            invocation.IsDryRun,
            cancellationToken,
            _loggerFactory,
            type => _services.TryGetValue(type, out var service) ? service : _registrationContext.GetService(type!),
            _options.DryRunSkipsJobPersistence,
            (user, metadata) => CanExecuteCapability(user, metadata));

        var capabilityContext = new CapabilityContext
        {
            Engine = engineContext,
            Request = requestContext,
            User = invocation.User,
            IsDryRun = invocation.IsDryRun
        };

        try
        {
            var result = await registered.Handler.ExecuteAsync(
                capabilityContext,
                parameters ?? new Dictionary<string, object?>(),
                cancellationToken).ConfigureAwait(false);

            var capabilityResult = result with { IsDryRun = invocation.IsDryRun };
            _options.AuditSink?.Write("capability_execute", requestContext, capabilityName, capabilityResult.Success);
            return new ExecutionResult(
                Success: capabilityResult.Success,
                CapabilityResult: capabilityResult,
                IsDryRun: invocation.IsDryRun);
        }
        catch (Exception ex)
        {
            _options.AuditSink?.Write("capability_execute", requestContext, capabilityName, false, ex.Message);
            return new ExecutionResult(Success: false, Error: ex.Message, IsDryRun: invocation.IsDryRun);
        }
    }

    private ExecutionResult? CheckConfirmation(
        CapabilityMetadata metadata,
        Invocation invocation,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        if (metadata.Risk is not (RiskLevel.ConfirmationRequired or RiskLevel.Destructive or RiskLevel.Admin))
        {
            return null;
        }

        if (invocation.IsDryRun)
        {
            return null;
        }

        var token = parameters?.TryGetValue("confirmationToken", out var value) == true ? value?.ToString() : null;
        if (_options.ConfirmationStore is not null
            && !string.IsNullOrWhiteSpace(token)
            && _options.ConfirmationStore.TryConsume(token!, metadata.Name, invocation.User.UserId))
        {
            return null;
        }

        var issued = _options.ConfirmationStore?.Issue(metadata.Name, invocation.User.UserId);
        return new ExecutionResult(
            Success: false,
            Error: "Confirmation required.",
            CapabilityResult: new CapabilityResult(
                Success: false,
                Data: new Dictionary<string, object?>
                {
                    ["confirmationRequired"] = true,
                    ["confirmationToken"] = issued,
                    ["capabilityName"] = metadata.Name
                },
                Message: $"Confirmation required for '{metadata.Name}'. Token: {issued}",
                IsDryRun: invocation.IsDryRun));
    }

    private bool CanExecuteCapability(UserContext user, CapabilityMetadata metadata) =>
        _options.AclService?.Allows(user, metadata) ?? true;

    private IReadOnlyList<CapabilityMetadata> FilterCapabilities(UserContext user, string? scope = null)
    {
        var all = _capabilities.GetAllMetadata();
        var filtered = _options.AclService?.FilterCapabilities(user, all) ?? all;
        if (string.IsNullOrWhiteSpace(scope))
        {
            return filtered;
        }

        return filtered
            .Where(c => string.Equals(c.Scope, scope, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Scope, "all", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private IJobTracker RequireJobTracker()
    {
        lock (_lifecycleGate)
        {
            if (!_isRunning || _jobTracker is null)
            {
                throw new InvalidOperationException("Engine is not running.");
            }

            return _jobTracker;
        }
    }

    private static IInternalBus CreateEventBus()
    {
        var auditDb = Environment.GetEnvironmentVariable("TORRENTBOT_AUDIT_DB");
        if (string.IsNullOrWhiteSpace(auditDb))
        {
            return new QueuedEventBus();
        }

        try
        {
            var outbox = new SqliteEventOutbox($"Data Source={auditDb}");
            return new QueuedEventBus(outbox: outbox);
        }
        catch
        {
            return new QueuedEventBus();
        }
    }

    private IInternalBus RequireBus()
    {
        lock (_lifecycleGate)
        {
            if (!_isRunning || _bus is null)
            {
                throw new InvalidOperationException("Engine is not running.");
            }

            return _bus;
        }
    }

    public CapabilityRegistry GetCapabilityRegistry() => _capabilities;

    public IInternalBus? GetInternalBus() => _bus;

    public string? ResolveCapabilityName(Invocation invocation) =>
        ResolveCapabilityInternal(invocation);

    public string? ResolveSlashCommand(string command) =>
        _capabilities.ResolveCommandFuzzy(command);

    public IReadOnlyList<CapabilityMetadata> FilterCapabilitiesForUser(UserContext user, string? scope = null) =>
        FilterCapabilities(user, scope);

    private string? ResolveCapabilityInternal(Invocation invocation)
    {
        if (!string.IsNullOrWhiteSpace(invocation.CapabilityName))
        {
            return invocation.CapabilityName;
        }

        if (!string.IsNullOrWhiteSpace(invocation.Command))
        {
            return _capabilities.ResolveCommand(invocation.Command);
        }

        return null;
    }
}
