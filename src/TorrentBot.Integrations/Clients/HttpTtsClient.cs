using System.Net.Http.Json;
using System.Text.Json;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;

namespace TorrentBot.Integrations.Clients;

public sealed class HttpTtsClient : ITtsClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly FakeTtsClient _fallback = new();

    public HttpTtsClient(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<TtsSpeakResult> SpeakAsync(string text, string? language = null, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/speak",
                new { text, language = language ?? "auto" },
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return await _fallback.SpeakAsync(text, language, ct).ConfigureAwait(false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var audioUrl = document.RootElement.TryGetProperty("audio_url", out var url) ? url.GetString() : null;
            return new TtsSpeakResult(true, audioUrl, "TTS request accepted");
        }
        catch (Exception)
        {
            return await _fallback.SpeakAsync(text, language, ct).ConfigureAwait(false);
        }
    }
}