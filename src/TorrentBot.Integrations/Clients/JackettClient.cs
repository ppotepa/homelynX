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
    private readonly string _indexers;
    private readonly ILogger<JackettClient> _logger;

    public JackettClient(
        HttpClient httpClient,
        string baseUrl,
        string? apiKey = null,
        string? indexers = null,
        ILogger<JackettClient>? logger = null)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _indexers = indexers ?? "all";
        _logger = logger ?? NullLogger<JackettClient>.Instance;
    }

    public async Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var allResults = new List<TorrentSearchResult>();
        var indexers = _indexers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var indexer in indexers)
        {
            try
            {
                var encodedQuery = Uri.EscapeDataString(query);
                var url = string.IsNullOrWhiteSpace(_apiKey)
                    ? $"{_baseUrl}/api/v2.0/indexers/{indexer}/results?Query={encodedQuery}"
                    : $"{_baseUrl}/api/v2.0/indexers/{indexer}/results?apikey={Uri.EscapeDataString(_apiKey)}&Query={encodedQuery}";

                using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Jackett search failed for {Indexer} with status {StatusCode}", indexer, response.StatusCode);
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                if (!document.RootElement.TryGetProperty("Results", out var resultsElement)
                    || resultsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in resultsElement.EnumerateArray())
                {
                    allResults.Add(new TorrentSearchResult(
                        Title: GetPropertyOrDefault(item, "Title") ?? "unknown",
                        MagnetUri: GetPropertyOrDefault(item, "MagnetUri") ?? string.Empty,
                        DownloadUrl: GetPropertyOrDefault(item, "Link") ?? GetPropertyOrDefault(item, "DownloadUrl"),
                        SizeBytes: GetPropertyOrDefaultLong(item, "Size"),
                        Seeders: GetPropertyOrDefaultInt(item, "Seeders"),
                        Indexer: GetPropertyOrDefault(item, "Tracker") ?? GetPropertyOrDefault(item, "Indexer") ?? indexer,
                        InfoHash: GetPropertyOrDefault(item, "InfoHash")));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogDebug(ex, "Jackett search error for indexer {Indexer}", indexer);
                continue;
            }
        }

        return allResults;
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