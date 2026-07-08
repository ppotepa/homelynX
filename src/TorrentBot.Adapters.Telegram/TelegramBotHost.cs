using TorrentBot.Adapters.Telegram.Verbosity;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Conversation;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Engine;
using TorrentBot.Engine.Context;
using TorrentBot.Engine.Pipeline;
using TorrentBot.Engine.Conversation;
using TorrentBot.Llm;
using TorrentBot.Presentation;

namespace TorrentBot.Adapters.Telegram;

public sealed record TelegramBotResponse(
    bool Success,
    string Message,
    ExecutionResult? ExecutionResult = null,
    LlmPipelineResult? LlmResult = null,
    RenderedOutput? Rendered = null,
    Contracts.Pipeline.ExecutionPlan? Plan = null);

public sealed class TelegramBotHost : IDisposable
{
    private readonly IInvocationPipeline _pipeline;
    private readonly IConversationPipeline _conversationPipeline;
    private readonly TelegramInvocationAdapter _adapter;
    private readonly ConversationResponseHandler _responseHandler;
    private readonly ConversationContextStore _conversationStore;
    private readonly VerbosityStageRecorder _verbosityRecorder;
    private readonly ArtifactPresentation _presentation;

    public TelegramBotHost(
        IEngine engine,
        IInvocationPipeline pipeline,
        IConversationPipeline? conversationPipeline = null,
        TelegramInvocationAdapter? adapter = null,
        ConversationResponseHandler? responseHandler = null,
        LlmPipeline? llmPipeline = null,
        ConversationContextStore? conversationStore = null,
        ArtifactPresentation? presentation = null)
    {
        _ = llmPipeline;
        if (engine is not EngineHost engineHost)
        {
            throw new ArgumentException("TelegramBotHost requires EngineHost.", nameof(engine));
        }

        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _conversationPipeline = conversationPipeline
            ?? throw new ArgumentNullException(nameof(conversationPipeline));
        _adapter = adapter ?? new TelegramInvocationAdapter();
        _conversationStore = conversationStore ?? engineHost.ConversationContextStore
            ?? throw new InvalidOperationException("ConversationContextStore is required.");
        _responseHandler = responseHandler ?? new ConversationResponseHandler(_conversationStore);
        _presentation = presentation ?? PresentationBootstrap.CreateDefault();
        _verbosityRecorder = new VerbosityStageRecorder(engine);
    }

    public VerbosityStageRecorder VerbosityRecorder => _verbosityRecorder;

    public async Task<TelegramBotResponse> HandleUpdateAsync(
        ITelegramUpdate update,
        UserContext user,
        bool isDryRun = false,
        CancellationToken cancellationToken = default,
        VerbosityStageRecorder? invocationRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(user);

        var recorder = invocationRecorder ?? _verbosityRecorder;
        var progress = invocationRecorder is not null ? (IProgressReporter)invocationRecorder : null;

        recorder.Record("parse", update.Text ?? update.CallbackData, invocationId: Guid.NewGuid().ToString("N"));

        var sessionId = update.ChatId.ToString();
        var conversation = _conversationStore.GetOrCreate(sessionId, user.UserId);
        var baseInvocation = CreateInvocation(_adapter.ToInvocation(update, user), isDryRun);

        var responseResolution = _responseHandler.Resolve(
            sessionId,
            user.UserId,
            update.IsCallback ? update.CallbackData : null,
            update.IsCallback ? null : update.Text);

        if (responseResolution.Handled)
        {
            if (!string.IsNullOrWhiteSpace(responseResolution.Error))
            {
                return new TelegramBotResponse(false, responseResolution.Error);
            }

            if (responseResolution.UserResponse is not null || responseResolution.DirectCapability is not null)
            {
                return await ExecuteUserResponseAsync(
                    conversation,
                    responseResolution,
                    baseInvocation,
                    cancellationToken,
                    recorder,
                    progress).ConfigureAwait(false);
            }
        }

        var invocation = CloneInvocation(baseInvocation, progress);

        if (!invocation.IsExplicit)
        {
            recorder.Record("plan", invocation.Text);
        }
        else
        {
            recorder.Record("execute", invocation.CapabilityName ?? invocation.Command);
        }

        var pipelineResult = await _pipeline.RunAsync(invocation, cancellationToken).ConfigureAwait(false);
        recorder.Record("respond", pipelineResult.Success ? "ok" : pipelineResult.Error);

        var rendered = _presentation.Render(
            pipelineResult.Artifacts,
            new RenderContext(RenderChannel.Telegram));

        return new TelegramBotResponse(
            pipelineResult.Success,
            rendered.Text,
            pipelineResult.Artifacts.RawResult,
            Rendered: rendered,
            Plan: pipelineResult.Plan);
    }

    private async Task<TelegramBotResponse> ExecuteUserResponseAsync(
        ConversationContext conversation,
        ConversationResponseResolution resolution,
        Invocation baseInvocation,
        CancellationToken cancellationToken,
        VerbosityStageRecorder recorder,
        IProgressReporter? progress)
    {
        PipelineResult? pipelineResult = null;

        if (resolution.UserResponse is not null)
        {
            recorder.Record("confirm", resolution.Cancelled ? "rejected" : "confirmed");
            pipelineResult = await _conversationPipeline.ProcessUserResponseAsync(
                resolution.UserResponse,
                conversation,
                CloneInvocation(baseInvocation, progress),
                cancellationToken).ConfigureAwait(false);
        }
        else if (resolution.DirectCapability is not null)
        {
            recorder.Record("execute", resolution.DirectCapability.CapabilityName);
            pipelineResult = await _conversationPipeline.ExecuteDirectCapabilityAsync(
                resolution.DirectCapability.CapabilityName,
                resolution.DirectCapability.Parameters,
                CloneInvocation(baseInvocation, progress),
                cancellationToken).ConfigureAwait(false);
        }

        recorder.Record("respond", pipelineResult?.Success == true ? "ok" : pipelineResult?.Error);

        var rendered = _presentation.Render(
            pipelineResult?.Artifacts ?? new ExecutionArtifacts(false, [], null, pipelineResult?.Error),
            new RenderContext(RenderChannel.Telegram));

        return new TelegramBotResponse(
            pipelineResult?.Success ?? false,
            rendered.Text,
            pipelineResult?.Artifacts.RawResult,
            Rendered: rendered,
            Plan: pipelineResult?.Plan);
    }

    private static Invocation CreateInvocation(Invocation invocation, bool isDryRun) =>
        new()
        {
            IsExplicit = invocation.IsExplicit,
            CapabilityName = invocation.CapabilityName,
            Command = invocation.Command,
            Text = invocation.Text,
            Parameters = invocation.Parameters,
            RequestContext = invocation.RequestContext,
            User = invocation.User,
            IsDryRun = isDryRun
        };

    private static Invocation CloneInvocation(Invocation invocation, IProgressReporter? progress) =>
        new()
        {
            IsExplicit = invocation.IsExplicit,
            CapabilityName = invocation.CapabilityName,
            Command = invocation.Command,
            Text = invocation.Text,
            Parameters = invocation.Parameters,
            RequestContext = invocation.RequestContext,
            User = invocation.User,
            IsDryRun = invocation.IsDryRun,
            Condition = invocation.Condition,
            ProgressReporter = progress
        };

    public void Dispose() => _verbosityRecorder.Dispose();
}