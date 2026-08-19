using System.Collections.Concurrent;
using System.Text.Json;
using TorrentBot.Acl;
using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Contracts.Invocation;
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
    private readonly ConcurrentDictionary<long, MediaSelection> _mediaSelections = new();

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
                hostEngine.GetInternalBus() ?? throw new InvalidOperationException("Engine internal bus is not initialized.")));
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
        mapped = await PrepareChiptuneAttachmentAsync(mapped, ct).ConfigureAwait(false);
        if (await TryHandleMediaInteractionAsync(mapped, progressMessageId, ct).ConfigureAwait(false))
        {
            return "Media selection handled";
        }

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

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = verbosity >= VerbosityLevel.Medium
            ? RunOrchestratorHeartbeatAsync(
                mapped.ChatId,
                progressMessageId,
                formatter,
                throttler,
                heartbeatCts.Token)
            : Task.CompletedTask;

        TelegramBotResponse response;
        try
        {
            response = await _host.HandleUpdateAsync(mapped, user, cancellationToken: ct, invocationRecorder: invocationRecorder).ConfigureAwait(false);
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeatTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }

        // Flush any pending progress edits before writing final response
        if (throttler is not null)
        {
            await throttler.FlushAsync().ConfigureAwait(false);
            throttler.Dispose();
        }
        invocationRecorder?.Dispose();

        var rendered = response.Rendered;

        if (response.Success && TryReadToolArtifact(response.ExecutionResult, out var artifact))
        {
            await DeliverTextAsync(mapped.ChatId, progressMessageId, response.Message, ct).ConfigureAwait(false);
            if (artifact.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                || artifact.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _messenger.SendPhotoAsync(mapped.ChatId, artifact.Content, artifact.FileName, ct).ConfigureAwait(false);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Telegram rejects oversized or very tall photos. Preserve the artifact as a document.
                    await _messenger.SendDocumentAsync(mapped.ChatId, artifact.Content, artifact.FileName, ct).ConfigureAwait(false);
                }
            }
            else if (artifact.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                await _messenger.SendAudioAsync(mapped.ChatId, new MemoryStream(artifact.Content, writable: false), artifact.FileName, ct).ConfigureAwait(false);
            else if (artifact.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                await _messenger.SendVideoAsync(mapped.ChatId, new MemoryStream(artifact.Content, writable: false), artifact.FileName, ct).ConfigureAwait(false);
            else
                await _messenger.SendDocumentAsync(mapped.ChatId, artifact.Content, artifact.FileName, ct).ConfigureAwait(false);

            if(TryReadToolActions(response.ExecutionResult,out var toolActions))
                await _messenger.SendTextAsync(mapped.ChatId,"Zmień wygenerowany chiptune:",toolActions,ct).ConfigureAwait(false);

            return response.Message;
        }

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

    private async Task RunOrchestratorHeartbeatAsync(
        long chatId,
        long progressMessageId,
        ProgressMessageFormatter? formatter,
        ProgressThrottler? throttler,
        CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var elapsed = DateTimeOffset.UtcNow - started;
                var detail = $"orchestrator nadal pracuje — elapsed {elapsed:mm\\:ss}";
                if (formatter is not null && throttler is not null)
                {
                    formatter.HandleStage("heartbeat", detail);
                    throttler.Submit(formatter.Format(), immediate: true);
                }
                else
                {
                    await _messenger.EditTextAsync(
                        chatId,
                        progressMessageId,
                        $"plan: submitting to orchestrator\n⏳ {detail}",
                        ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Progress must never turn a successful orchestrator response into an error.
        }
    }

    private async Task<ITelegramUpdate> PrepareChiptuneAttachmentAsync(ITelegramUpdate update, CancellationToken ct)
    {
        if (update.Attachment is null || update.Text is null || !update.Text.TrimStart().StartsWith("/chiptune", StringComparison.OrdinalIgnoreCase)) return update;
        const int maxMidiBytes = 5 * 1024 * 1024;
        var bytes = await _messenger.DownloadFileAsync(update.Attachment.FileId, ct).ConfigureAwait(false);
        if (bytes.Length > maxMidiBytes) throw new InvalidOperationException("MIDI file is larger than the 5 MB limit.");
        var text = $"{update.Text} midi_base64={Convert.ToBase64String(bytes)}";
        return new TelegramUpdate(update.ChatId, update.UserId, text, update.MessageId, update.CallbackData, update.Attachment);
    }

    private static bool TryReadToolArtifact(ExecutionResult? result, out ToolArtifact artifact)
    {
        artifact = default;
        if (result?.CapabilityResult?.Data is not IDictionary<string, object?> data
            || !TryGetValue(data, "toolArtifact", out var rawValue)
            || !TryGetString(rawValue, "fileName", out var fileName)
            || !TryGetString(rawValue, "contentType", out var contentType)
            || !TryGetString(rawValue, "contentBase64", out var encoded))
        {
            return false;
        }

        try
        {
            artifact = new ToolArtifact(fileName, contentType, Convert.FromBase64String(encoded));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryGetValue(IDictionary<string, object?> values, string key, out object? value)
        => values.TryGetValue(key, out value);

    private static bool TryGetString(object? value, string key, out string text)
    {
        if (value is IDictionary<string, object?> dictionary && dictionary.TryGetValue(key, out var nested) && nested is string stringValue)
        {
            text = stringValue;
            return true;
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String)
        {
            text = property.GetString()!;
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static bool TryReadToolActions(ExecutionResult? result,out IReadOnlyList<TelegramInlineButton> actions)
    {
        var list=new List<TelegramInlineButton>();actions=list;
        if(result?.CapabilityResult?.Data is not IDictionary<string,object?> data||!data.TryGetValue("toolActions",out var raw)||raw is not System.Collections.IEnumerable values)return false;
        foreach(var value in values)
            if(TryGetString(value,"text",out var label)&&TryGetString(value,"callbackData",out var callback)&&callback.Length<=64)list.Add(new TelegramInlineButton(label,callback));
        return list.Count>0;
    }

    private readonly record struct ToolArtifact(string FileName, string ContentType, byte[] Content);

    private async Task<bool> TryHandleMediaInteractionAsync(ITelegramUpdate mapped, long progressMessageId, CancellationToken ct)
    {
        if (mapped.IsCallback && mapped.CallbackData is { } callback)
        {
            if (callback.StartsWith("media-format:", StringComparison.OrdinalIgnoreCase)
                && _mediaSelections.TryGetValue(mapped.ChatId, out var pending))
            {
                var selectedFormat = callback["media-format:".Length..].ToLowerInvariant();
                if (selectedFormat is not ("mp3" or "mp4"))
                {
                    return true;
                }

                _mediaSelections[mapped.ChatId] = pending with { Format = selectedFormat };
                var qualities = selectedFormat == "mp3"
                    ? new[] { ("128 kbps", "128"), ("192 kbps", "192"), ("320 kbps", "320") }
                    : new[] { ("360p", "360"), ("480p", "480"), ("720p", "720"), ("1080p", "1080") };
                var buttons = qualities
                    .Select(item => new TelegramInlineButton(item.Item1, $"media-quality:{item.Item2}"))
                    .ToArray();
                await _messenger.SendTextAsync(mapped.ChatId, $"Wybierz jakość dla {selectedFormat.ToUpperInvariant()}:", buttons, ct).ConfigureAwait(false);
                return true;
            }

            if (callback.StartsWith("media-quality:", StringComparison.OrdinalIgnoreCase)
                && _mediaSelections.TryRemove(mapped.ChatId, out var selected)
                && selected.Format is not null)
            {
                var quality = callback["media-quality:".Length..];
                var synthetic = new TelegramUpdate(
                    mapped.ChatId,
                    mapped.UserId,
                    $"/download_media {selected.Url} {selected.Format} {quality}{selected.ClipArguments}",
                    mapped.MessageId);
                await HandleMappedUpdateAsync(synthetic, progressMessageId, ct).ConfigureAwait(false);
                return true;
            }
        }

        if (!mapped.IsCallback && TryGetMediaSelection(mapped.Text, out var url, out var format, out var clipArguments))
        {
            if (format is not null)
            {
                return false;
            }

            _mediaSelections[mapped.ChatId] = new MediaSelection(url, null, clipArguments);
            var buttons = new[]
            {
                new TelegramInlineButton("MP3", "media-format:mp3"),
                new TelegramInlineButton("MP4", "media-format:mp4")
            };
            await _messenger.SendTextAsync(mapped.ChatId, "Co chcesz pobrać?", buttons, ct).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private static bool TryGetMediaSelection(string? text, out string url, out string? format, out string clipArguments)
    {
        url = string.Empty;
        format = null;
        clipArguments = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.TrimStart().StartsWith('/')
            && !text.TrimStart().StartsWith("/download_media", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var urlToken = tokens.FirstOrDefault(token => Uri.TryCreate(token, UriKind.Absolute, out _));
        if (!IsSupportedMediaUrl(urlToken))
        {
            return false;
        }

        url = urlToken!;
        format = tokens.FirstOrDefault(token => token.Equals("mp3", StringComparison.OrdinalIgnoreCase)
            || token.Equals("mp4", StringComparison.OrdinalIgnoreCase)
            || token.Equals("subtitles", StringComparison.OrdinalIgnoreCase)
            || token.Equals("subs", StringComparison.OrdinalIgnoreCase))?.ToLowerInvariant();
        if (format is "subtitles" or "subs")
        {
            return true;
        }
        if (SlashCommandRouting.TryParseClip(text, out var start, out var end))
        {
            clipArguments = $" clip {start} {end}";
        }
        return true;
    }

    private static bool IsSupportedMediaUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var hosts = new[] { "youtube.com", "youtu.be", "facebook.com", "fb.watch", "dailymotion.com", "dai.ly", "vimeo.com", "instagram.com", "tiktok.com" };
        return hosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record MediaSelection(string Url, string? Format, string ClipArguments = "");

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
