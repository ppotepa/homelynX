using TorrentBot.Adapters.Telegram.Verbosity;
using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Engine;
using TorrentBot.Engine.Confirmations;
using TorrentBot.Llm;
using TorrentBot.Presentation;

namespace TorrentBot.Adapters.Telegram;

public sealed record TelegramBotResponse(
    bool Success,
    string Message,
    ExecutionResult? ExecutionResult = null,
    LlmPipelineResult? LlmResult = null,
    RenderedOutput? Rendered = null);

public sealed class TelegramBotHost : IDisposable
{
    private readonly IInvocationPipeline _pipeline;
    private readonly TelegramInvocationAdapter _adapter;
    private readonly ConfirmationCallbackHandler _confirmationHandler;
    private readonly VerbosityStageRecorder _verbosityRecorder;
    private readonly ArtifactPresentation _presentation;

    public TelegramBotHost(
        IEngine engine,
        IInvocationPipeline? pipeline = null,
        TelegramInvocationAdapter? adapter = null,
        ConfirmationCallbackHandler? confirmationHandler = null,
        LlmPipeline? llmPipeline = null,
        ConfirmationStore? confirmationStore = null,
        PendingInvocationStore? pendingInvocationStore = null,
        ArtifactPresentation? presentation = null)
    {
        _ = llmPipeline;
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _adapter = adapter ?? new TelegramInvocationAdapter();
        _confirmationHandler = confirmationHandler
            ?? new ConfirmationCallbackHandler(confirmationStore, pendingInvocationStore);
        _presentation = presentation ?? PresentationBootstrap.CreateDefault();
        _verbosityRecorder = new VerbosityStageRecorder(engine);
    }

    public VerbosityStageRecorder VerbosityRecorder => _verbosityRecorder;

    public async Task<TelegramBotResponse> HandleUpdateAsync(
        ITelegramUpdate update,
        UserContext user,
        bool isDryRun = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(user);

        _verbosityRecorder.Record("parse", update.Text ?? update.CallbackData, invocationId: Guid.NewGuid().ToString("N"));

        var confirmation = _confirmationHandler.Resolve(update);
        if (confirmation.Handled)
        {
            _verbosityRecorder.Record("confirm", confirmation.Confirmed ? "confirmed" : "rejected");
            if (!confirmation.Confirmed)
            {
                return new TelegramBotResponse(false, confirmation.Error ?? "Action was not confirmed.");
            }

            if (confirmation.PendingInvocation is not null)
            {
                return await ExecuteConfirmedInvocationAsync(confirmation.PendingInvocation, confirmation.Token!, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var invocation = CreateInvocation(_adapter.ToInvocation(update, user), isDryRun);
        if (!invocation.IsExplicit)
        {
            _verbosityRecorder.Record("plan", invocation.Text);
        }
        else
        {
            _verbosityRecorder.Record("execute", invocation.CapabilityName ?? invocation.Command);
        }

        var pipelineResult = await _pipeline.RunAsync(invocation, cancellationToken).ConfigureAwait(false);
        _verbosityRecorder.Record("respond", pipelineResult.Success ? "ok" : pipelineResult.Error);

        RegisterConfirmationIfNeeded(invocation, pipelineResult);

        var rendered = _presentation.Render(
            pipelineResult.Artifacts,
            new RenderContext(RenderChannel.Telegram));

        return new TelegramBotResponse(
            pipelineResult.Success,
            rendered.Text,
            pipelineResult.Artifacts.RawResult,
            Rendered: rendered);
    }

    private async Task<TelegramBotResponse> ExecuteConfirmedInvocationAsync(
        PendingInvocation pending,
        string token,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (pending.Parameters is not null)
        {
            foreach (var (key, value) in pending.Parameters)
            {
                parameters[key] = value;
            }
        }

        parameters["confirmationToken"] = token;

        var invocation = new Invocation
        {
            IsExplicit = true,
            CapabilityName = pending.CapabilityName,
            Parameters = parameters,
            RequestContext = pending.RequestContext,
            User = pending.User,
            IsDryRun = pending.IsDryRun
        };

        _verbosityRecorder.Record("execute", pending.CapabilityName);
        var pipelineResult = await _pipeline.RunAsync(invocation, cancellationToken).ConfigureAwait(false);
        _verbosityRecorder.Record("respond", pipelineResult.Success ? "ok" : pipelineResult.Error);

        var rendered = _presentation.Render(
            pipelineResult.Artifacts,
            new RenderContext(RenderChannel.Telegram));

        return new TelegramBotResponse(
            pipelineResult.Success,
            rendered.Text,
            pipelineResult.Artifacts.RawResult,
            Rendered: rendered);
    }

    private void RegisterConfirmationIfNeeded(Invocation invocation, PipelineResult pipelineResult)
    {
        var confirm = pipelineResult.Artifacts.Items.OfType<ConfirmationArtifact>().FirstOrDefault();
        if (confirm is null)
        {
            return;
        }

        _confirmationHandler.RegisterPending(
            confirm.Token,
            new PendingInvocation(
                invocation.CapabilityName ?? confirm.CapabilityName,
                invocation.Parameters,
                invocation.RequestContext,
                invocation.User,
                invocation.IsDryRun));
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

    public void Dispose() => _verbosityRecorder.Dispose();
}