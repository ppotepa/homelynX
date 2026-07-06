using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Plugins.Media.Capabilities;

public sealed class TtsSayHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var text = parameters.TryGetValue("text", out var value) ? value?.ToString() : "hello";
        var client = context.Engine.GetService<ITtsClient>();
        if (client is null)
        {
            return new CapabilityResult(Success: false, Message: "TTS client is not available.", IsDryRun: context.IsDryRun);
        }

        var result = await client.SpeakAsync(text ?? "hello", ct: cancellationToken).ConfigureAwait(false);
        return new CapabilityResult(
            Success: result.Success,
            Data: new Dictionary<string, object?> { ["spoken"] = text, ["audio_url"] = result.AudioUrl, ["service"] = "tts-http" },
            Message: result.Message ?? $"TTS spoke: {text}",
            IsDryRun: context.IsDryRun);
    }
}