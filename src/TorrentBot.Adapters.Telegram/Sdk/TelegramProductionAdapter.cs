using TorrentBot.Acl;
using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Engine;
using TorrentBot.Engine.Confirmations;
using TorrentBot.Engine.Conversation;
using TorrentBot.Presentation;
using Telegram.Bot.Types;
using TorrentBot.Adapters.Telegram.Verbosity;

namespace TorrentBot.Adapters.Telegram.Sdk;

public sealed class TelegramProductionAdapter
{
    private readonly TelegramBotHost _host;
    private readonly ITelegramMessenger _messenger;
    private readonly AclService _acl;
    private readonly VerbositySettingsStore _verbositySettings;

    public TelegramProductionAdapter(
        IEngine engine,
        ITelegramMessenger messenger,
        AclService? acl = null,
        VerbositySettingsStore? verbositySettings = null,
        IInvocationPipeline? pipeline = null,
        ArtifactPresentation? presentation = null)
    {
        _messenger = messenger;
        _acl = acl ?? AclService.FromEnvironment();
        _verbositySettings = verbositySettings ?? new VerbositySettingsStore();
        var hostEngine = engine as EngineHost
            ?? throw new ArgumentException("Telegram adapter requires EngineHost.", nameof(engine));
        var services = pipeline is null
            ? PipelineBootstrap.Create(hostEngine)
            : new PipelineServices(pipeline, new ConversationPipeline(
                hostEngine,
                pipeline,
                hostEngine.GetCapabilityRegistry(),
                hostEngine.GetInternalBus()));
        var invocationAdapter = new TelegramInvocationAdapter(hostEngine.ResolveSlashCommand);
        _host = new TelegramBotHost(
            engine,
            services.Invocation,
            conversationPipeline: services.Conversation,
            adapter: invocationAdapter,
            conversationStore: hostEngine.ConversationContextStore,
            presentation: presentation ?? PresentationBootstrap.CreateDefault());
    }

    public VerbosityStageRecorder VerbosityRecorder => _host.VerbosityRecorder;

    public async Task HandleUpdateAsync(Update update, CancellationToken ct = default)
    {
        var mapped = TelegramSdkUpdateMapper.Map(update);
        if (mapped is null)
        {
            return;
        }

        var progressMessageId = update.CallbackQuery?.Message?.MessageId
            ?? await _messenger.SendTextAsync(mapped.ChatId, "Working...", ct: ct).ConfigureAwait(false);

        await HandleMappedUpdateAsync(mapped, progressMessageId, ct).ConfigureAwait(false);
    }

    public async Task<string> HandleMappedUpdateAsync(ITelegramUpdate mapped, long progressMessageId, CancellationToken ct = default)
    {
        if (VerbositySettingsStore.TryParse(mapped.Text, out var level))
        {
            _verbositySettings.Set(mapped.ChatId, level);
            var ack = $"Verbosity set to {level}.";
            await DeliverTextAsync(mapped.ChatId, progressMessageId, ack, ct).ConfigureAwait(false);
            return ack;
        }

        var verbosity = _verbositySettings.Get(mapped.ChatId);
        var user = _acl.ResolveUser(mapped.UserId);

        // For verbosity Full/Debug, create per-invocation recorder with real-time progress
        VerbosityStageRecorder? invocationRecorder = null;
        ProgressThrottler? throttler = null;
        ProgressMessageFormatter? formatter = null;

        if (verbosity >= VerbosityLevel.Full)
        {
            invocationRecorder = new VerbosityStageRecorder();
            throttler = new ProgressThrottler(TimeSpan.FromSeconds(1));
            formatter = new ProgressMessageFormatter();
            formatter.SetUserText(mapped.Text ?? mapped.CallbackData ?? "(callback)");

            throttler.Configure(async (text, token) =>
            {
                try
                {
                    await _messenger.EditTextAsync(mapped.ChatId, progressMessageId, text, token).ConfigureAwait(false);
                }
                catch
                {
                    // Edit failed (e.g., content unchanged or rate limited) — ignore
                }
            }, ct);

            invocationRecorder.OnStage += msg =>
            {
                formatter.HandleStage(msg.Stage, msg.Detail);
                var text = formatter.Format(includeDebugArtifacts: false);
                var isImmediate = msg.Stage.Contains("error", StringComparison.OrdinalIgnoreCase)
                    || msg.Stage == "step:error"
                    || msg.Stage == "planning:done"
                    || msg.Stage == "respond"
                    || msg.Stage == "debug:pipeline:complete";
                throttler.Submit(text, immediate: isImmediate);
            };
        }
        else if (verbosity >= VerbosityLevel.Low)
        {
            await DeliverTextAsync(mapped.ChatId, progressMessageId, "parse: received update", ct).ConfigureAwait(false);
        }

        if (verbosity >= VerbosityLevel.Medium && verbosity < VerbosityLevel.Full)
        {
            await DeliverTextAsync(mapped.ChatId, progressMessageId, "plan: submitting to orchestrator", ct).ConfigureAwait(false);
        }

        var response = await _host.HandleUpdateAsync(mapped, user, cancellationToken: ct, invocationRecorder: invocationRecorder).ConfigureAwait(false);

        // Flush any pending progress edits before writing final response
        if (throttler is not null)
        {
            await throttler.FlushAsync().ConfigureAwait(false);
            throttler.Dispose();
        }
        invocationRecorder?.Dispose();

        var rendered = response.Rendered;

        if (rendered?.Buttons is { Count: > 0 } renderedButtons
            && renderedButtons.Any(b => b.CallbackData.StartsWith("pending:", StringComparison.OrdinalIgnoreCase)))
        {
            var buttons = renderedButtons
                .Select(b => new TelegramInlineButton(b.Text, b.CallbackData))
                .ToArray();
            await _messenger.SendTextAsync(mapped.ChatId, TelegramMessageLimits.Truncate(rendered.Text), buttons, ct).ConfigureAwait(false);
            return rendered.Text;
        }

        var finalText = verbosity >= VerbosityLevel.Full
            ? BuildVerboseResponse(mapped, response, rendered, formatter)
            : rendered?.Text ?? response.Message;
        finalText = TelegramMessageLimits.Truncate(finalText);

        if (rendered?.Buttons is { Count: > 0 } actionButtons)
        {
            if (verbosity >= VerbosityLevel.Full && formatter is not null)
            {
                await DeliverTextAsync(
                    mapped.ChatId,
                    progressMessageId,
                    formatter.Format(includeDebugArtifacts: true),
                    ct).ConfigureAwait(false);
            }

            var buttons = actionButtons.Select(b => new TelegramInlineButton(b.Text, b.CallbackData)).ToArray();
            var actionText = verbosity >= VerbosityLevel.Full
                ? TelegramMessageLimits.Truncate(rendered?.Text ?? response.Message)
                : finalText;
            await _messenger.SendTextAsync(mapped.ChatId, actionText, buttons, ct).ConfigureAwait(false);
            return actionText;
        }

        await DeliverTextAsync(mapped.ChatId, progressMessageId, finalText, ct).ConfigureAwait(false);

        return finalText;
    }

    private async Task DeliverTextAsync(long chatId, long messageId, string text, CancellationToken ct)
    {
        var safeText = TelegramMessageLimits.Truncate(text);
        try
        {
            await _messenger.EditTextAsync(chatId, messageId, safeText, ct).ConfigureAwait(false);
        }
        catch
        {
            await _messenger.SendTextAsync(chatId, safeText, ct: ct).ConfigureAwait(false);
        }
    }

    private static string BuildVerboseResponse(ITelegramUpdate mapped, TelegramBotResponse response, RenderedOutput? rendered, ProgressMessageFormatter? formatter = null)
    {
        var sb = new System.Text.StringBuilder();

        // If we have a formatter with progress stages, include them
        if (formatter is not null)
        {
            sb.AppendLine("🔍 VERBOSE MODE (Debug)");
            sb.AppendLine($"🕒 {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}Z | chat={mapped.ChatId}");
            sb.AppendLine();
            sb.AppendLine(formatter.Format());
            sb.AppendLine();
            sb.AppendLine("───");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("🔍 VERBOSE MODE");
            sb.AppendLine();
            sb.AppendLine($"📝 Request: {mapped.Text ?? mapped.CallbackData ?? "(callback)"}");
            sb.AppendLine();
        }

        // Show plan info if available
        if (response.Plan is { } plan)
        {
            sb.AppendLine($"🎯 Plan: {plan.Intent}");
            sb.AppendLine($"   Steps: {plan.Steps.Count}");
            foreach (var step in plan.Steps)
            {
                sb.AppendLine($"   • {step.CapabilityName}");
                if (step.Parameters is { Count: > 0 })
                {
                    var paramStr = string.Join(", ", step.Parameters.Take(3).Select(p => $"{p.Key}={p.Value}"));
                    sb.AppendLine($"     params: {paramStr}");
                }
            }
            sb.AppendLine();
        }

        // Show execution result
        sb.AppendLine($"✅ Execution: {(response.ExecutionResult?.Success == true ? "Success" : "Failed")}");
        if (response.ExecutionResult?.Error is string error)
        {
            sb.AppendLine($"   Error: {error}");
        }
        sb.AppendLine();

        // Show response
        sb.AppendLine("💬 Response:");
        sb.AppendLine(rendered?.Text ?? response.Message ?? "(no content)");

        return sb.ToString();
    }
}
