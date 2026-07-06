using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Integrations.Fakes;

public sealed class FakeTtsClient : ITtsClient
{
    public Task<TtsSpeakResult> SpeakAsync(string text, string? language = null, CancellationToken ct = default) =>
        Task.FromResult(new TtsSpeakResult(true, "http://tts.local/audio/fake.wav", $"Spoke '{text}' ({language ?? "auto"})"));
}