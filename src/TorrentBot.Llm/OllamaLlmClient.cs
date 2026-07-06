using System.Net.Http.Json;
using System.Text.Json;

namespace TorrentBot.Llm;

public sealed class OllamaLlmClient
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _baseUrl;

    public OllamaLlmClient(HttpClient httpClient, string baseUrl = "http://localhost:11434", string model = "llama3")
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/generate",
                new
                {
                    model = _model,
                    prompt,
                    stream = false
                },
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return string.Empty;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return document.RootElement.TryGetProperty("response", out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return string.Empty;
        }
    }
}