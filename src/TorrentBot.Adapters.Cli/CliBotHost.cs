using System.Diagnostics;
using TorrentBot.Acl;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Engine;
using TorrentBot.Engine.Context;
using TorrentBot.Presentation;

namespace TorrentBot.Adapters.Cli;

public sealed record CliBotResponse(
    bool Success,
    string Message,
    ExecutionResult? ExecutionResult = null,
    RenderedOutput? Rendered = null,
    Contracts.Pipeline.ExecutionPlan? Plan = null,
    TimeSpan Duration = default,
    int RequestNumber = 0);

public sealed class CliBotHost : IAsyncDisposable
{
    private readonly IInvocationPipeline _pipeline;
    private readonly CliInvocationAdapter _adapter;
    private readonly ArtifactPresentation _presentation;
    private readonly ConversationContextStore _contextStore;
    private readonly AclService _acl;
    private readonly Dictionary<string, ConversationContext> _sessions = new();
    private readonly Stopwatch _requestTimer = new();

    public CliBotHost(
        IEngine engine,
        AclService? acl = null,
        IInvocationPipeline? pipeline = null,
        ArtifactPresentation? presentation = null,
        ConversationContextStore? contextStore = null)
    {
        _acl = acl ?? AclService.FromEnvironment();
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _adapter = new CliInvocationAdapter(command =>
            engine is EngineHost host ? host.ResolveSlashCommand(command) : null);
        _presentation = presentation ?? PresentationBootstrap.CreateDefault();
        _contextStore = contextStore ?? new ConversationContextStore();
    }

    public async Task<CliBotResponse> HandleMessageAsync(
        string text,
        string userId,
        string sessionId = "cli-session",
        bool isDryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(userId);

        _requestTimer.Restart();

        var user = _acl.ResolveUser(userId);
        var context = GetOrCreateContext(sessionId, userId);
        var requestNumber = context.NextRequestNumber();

        context.AddMessage("user", text, requestNumber);

        var invocation = _adapter.ToInvocation(text, user, isDryRun);
        var pipelineResult = await _pipeline.RunAsync(invocation, cancellationToken).ConfigureAwait(false);

        var rendered = _presentation.Render(
            pipelineResult.Artifacts,
            new RenderContext(RenderChannel.Cli));

        context.AddMessage("assistant", rendered.Text, requestNumber);

        _requestTimer.Stop();

        return new CliBotResponse(
            pipelineResult.Success,
            rendered.Text,
            pipelineResult.Artifacts.RawResult,
            rendered,
            null,
            _requestTimer.Elapsed,
            requestNumber);
    }

    private ConversationContext GetOrCreateContext(string sessionId, string userId)
    {
        if (!_sessions.TryGetValue(sessionId, out var context))
        {
            context = new ConversationContext(sessionId, userId);
            _sessions[sessionId] = context;
        }
        return context;
    }

    public ValueTask DisposeAsync()
    {
        _sessions.Clear();
        return ValueTask.CompletedTask;
    }
}
