using TorrentBot.Acl;
using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Engine;
using TorrentBot.Engine.Confirmations;
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
        ConfirmationStore? confirmationStore = null,
        PendingInvocationStore? pendingStore = null,
        VerbositySettingsStore? verbositySettings = null,
        IInvocationPipeline? pipeline = null,
        ArtifactPresentation? presentation = null)
    {
        _messenger = messenger;
        _acl = acl ?? AclService.FromEnvironment();
        _verbositySettings = verbositySettings ?? new VerbositySettingsStore();
        var hostEngine = engine as EngineHost
            ?? throw new ArgumentException("Telegram adapter requires EngineHost.", nameof(engine));
        var resolvedPipeline = pipeline ?? PipelineBootstrap.Create(hostEngine, hostEngine.LlmPipeline);
        var invocationAdapter = new TelegramInvocationAdapter(hostEngine.ResolveSlashCommand);
        _host = new TelegramBotHost(
            engine,
            resolvedPipeline,
            adapter: invocationAdapter,
            confirmationStore: confirmationStore,
            pendingInvocationStore: pendingStore,
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

        // Telegram only allows editing messages sent by the bot. Callback messages are bot-owned;
        // plain user commands must get a separate progress message first.
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

        if (verbosity >= VerbosityLevel.Low)
        {
            await DeliverTextAsync(mapped.ChatId, progressMessageId, "parse: received update", ct).ConfigureAwait(false);
        }

        if (verbosity >= VerbosityLevel.Medium)
        {
            await DeliverTextAsync(mapped.ChatId, progressMessageId, "plan: submitting to orchestrator", ct).ConfigureAwait(false);
        }

        var response = await _host.HandleUpdateAsync(mapped, user, cancellationToken: ct).ConfigureAwait(false);
        var rendered = response.Rendered;

        if (rendered?.Buttons is { Count: > 0 } renderedButtons
            && renderedButtons.Any(b => b.CallbackData.StartsWith("confirm:", StringComparison.OrdinalIgnoreCase)))
        {
            var buttons = renderedButtons
                .Select(b => new TelegramInlineButton(b.Text, b.CallbackData))
                .ToArray();
            await _messenger.SendTextAsync(mapped.ChatId, rendered.Text, buttons, ct).ConfigureAwait(false);
            return rendered.Text;
        }

        if (TryExtractDeliverableMedia(response.ExecutionResult?.CapabilityResult?.Data, out var media))
        {
            if (media.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                await _messenger.SendPhotoAsync(mapped.ChatId, media.Content, media.FileName, ct).ConfigureAwait(false);
            }
            else
            {
                await _messenger.SendDocumentAsync(mapped.ChatId, media.Content, media.FileName, ct).ConfigureAwait(false);
            }
        }

        var finalText = verbosity >= VerbosityLevel.Full
            ? $"execute: done\nrespond: {rendered?.Text ?? response.Message}"
            : rendered?.Text ?? response.Message;

        if (rendered?.Buttons is { Count: > 0 } actionButtons)
        {
            var buttons = actionButtons.Select(b => new TelegramInlineButton(b.Text, b.CallbackData)).ToArray();
            await _messenger.SendTextAsync(mapped.ChatId, finalText, buttons, ct).ConfigureAwait(false);
            return finalText;
        }

        await DeliverTextAsync(mapped.ChatId, progressMessageId, finalText, ct).ConfigureAwait(false);

        return finalText;
    }

    private async Task DeliverTextAsync(long chatId, long messageId, string text, CancellationToken ct)
    {
        try
        {
            await _messenger.EditTextAsync(chatId, messageId, text, ct).ConfigureAwait(false);
        }
        catch
        {
            await _messenger.SendTextAsync(chatId, text, ct: ct).ConfigureAwait(false);
        }
    }

    private static bool TryExtractDeliverableMedia(object? data, out SurveillanceMediaDelivery media)
    {
        media = default!;
        if (data is not Dictionary<string, object?> dict)
        {
            return false;
        }

        if (dict.TryGetValue("deliverable", out var deliverable)
            && deliverable is bool ok
            && ok
            && dict.TryGetValue("base64", out var encoded)
            && encoded is string base64
            && dict.TryGetValue("file_name", out var fileName)
            && fileName is string name
            && dict.TryGetValue("content_type", out var contentType)
            && contentType is string type)
        {
            media = new SurveillanceMediaDelivery(Convert.FromBase64String(base64), type, name);
            return true;
        }

        return false;
    }

    private sealed record SurveillanceMediaDelivery(byte[] Content, string ContentType, string FileName);
}