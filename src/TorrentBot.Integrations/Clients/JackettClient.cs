using System.Collections.Concurrent;
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
    private readonly string[] _indexers;
    private readonly TimeSpan _perIndexerTimeout;
    private readonly ILogger<JackettClient> _logger;

    public JackettClient(
        HttpClient httpClient,
        string baseUrl,
        string? apiKey = null,
        string? indexers = null,
        ILogger<JackettClient>? logger = null,
        TimeSpan? perIndexerTimeout = null)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _indexers = (indexers ?? "all")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _logger = logger ?? NullLogger<JackettClient>.Instance;
        _perIndexerTimeout = perIndexerTimeout ?? TimeSpan.FromSeconds(20);
    }

    public async Task<IReadOnlyList<TorrentSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var bag = new ConcurrentBag<TorrentSearchResult>();
        var tasks = _indexers.Select(indexer => SearchIndexerAsync(indexer, query, bag, ct)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return DeduplicateResults(bag);
    }

    private async Task SearchIndexerAsync(
        string indexer,
        string query,
        ConcurrentBag<TorrentSearchResult> bag,
        CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_perIndexerTimeout);

            var encodedQuery = Uri.EscapeDataString(query);
            var url = string.IsNullOrWhiteSpace(_apiKey)
                ? $"{_baseUrl}/api/v2.0/indexers/{indexer}/results?Query={encodedQuery}"
                : $"{_baseUrl}/api/v2.0/indexers/{indexer}/results?apikey={Uri.EscapeDataString(_apiKey)}&Query={encodedQuery}";

            using var response = await _httpClient.GetAsync(url, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Jackett search failed for {Indexer} with status {StatusCode}", indexer, response.StatusCode);
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("Results", out var resultsElement)
                || resultsElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in resultsElement.EnumerateArray())
            {
                bag.Add(new TorrentSearchResult(
                    Title: GetPropertyOrDefault(item, "Title") ?? "unknown",
                    MagnetUri: GetPropertyOrDefault(item, "MagnetUri") ?? string.Empty,
                    DownloadUrl: GetPropertyOrDefault(item, "Link") ?? GetPropertyOrDefault(item, "DownloadUrl"),
                    SizeBytes: GetPropertyOrDefaultLong(item, "Size"),
                    Seeders: GetPropertyOrDefaultInt(item, "Seeders"),
                    Indexer: GetPropertyOrDefault(item, "Tracker") ?? GetPropertyOrDefault(item, "Indexer") ?? indexer,
                    InfoHash: GetPropertyOrDefault(item, "InfoHash")));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Jackett search error for indexer {Indexer}", indexer);
        }
    }

    private static List<TorrentSearchResult> DeduplicateResults(IEnumerable<TorrentSearchResult> results)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<TorrentSearchResult>();

        foreach (var result in results.OrderByDescending(r => r.Seeders))
        {
            var key = !string.IsNullOrWhiteSpace(result.InfoHash)
                ? result.InfoHash
                : result.Title.Trim();
            if (!seen.Add(key))
            {
                continue;
            }

            deduped.Add(result);
        }

        return deduped;
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