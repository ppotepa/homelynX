using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Integrations.Models;

namespace TorrentBot.Integrations.Clients;

public sealed class JackettClient : IJackettClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly ILogger<JackettClient> _logger;

    public JackettClient(
        HttpClient httpClient,
        string baseUrl,
        string? apiKey = null,
        ILogger<JackettClient>? logger = null)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _logger = logger ?? NullLogger<JackettClient>.Instance;
    }

    public async Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = string.IsNullOrWhiteSpace(_apiKey)
                ? $"{_baseUrl}/api/v2.0/indexers/all/results?Query={encodedQuery}"
                : $"{_baseUrl}/api/v2.0/indexers/all/results?apikey={Uri.EscapeDataString(_apiKey)}&Query={encodedQuery}";

            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Jackett search failed with status {StatusCode}", response.StatusCode);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("Results", out var resultsElement)
                || resultsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<TorrentSearchResult>();
            foreach (var item in resultsElement.EnumerateArray())
            {
                results.Add(new TorrentSearchResult(
                    Title: GetPropertyOrDefault(item, "Title") ?? "unknown",
                    MagnetUri: GetPropertyOrDefault(item, "MagnetUri") ?? string.Empty,
                    DownloadUrl: GetPropertyOrDefault(item, "Link") ?? GetPropertyOrDefault(item, "DownloadUrl"),
                    SizeBytes: GetPropertyOrDefaultLong(item, "Size"),
                    Seeders: GetPropertyOrDefaultInt(item, "Seeders"),
                    Indexer: GetPropertyOrDefault(item, "Tracker") ?? GetPropertyOrDefault(item, "Indexer") ?? "unknown",
                    InfoHash: GetPropertyOrDefault(item, "InfoHash")));
            }

            return results;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Jackett search failed gracefully for query '{Query}'", query);
            return [];
        }
    }

    private static string? GetPropertyOrDefault(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long GetPropertyOrDefaultLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
            ? number
            : 0;

    private static int GetPropertyOrDefaultInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;
}