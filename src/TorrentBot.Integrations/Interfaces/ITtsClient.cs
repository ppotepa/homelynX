namespace TorrentBot.Integrations.Interfaces;

public sealed record TtsSpeakResult(bool Success, string? AudioUrl, string? Message);

public interface ITtsClient
{
    Task<TtsSpeakResult> SpeakAsync(string text, string? language = null, CancellationToken ct = default);
}