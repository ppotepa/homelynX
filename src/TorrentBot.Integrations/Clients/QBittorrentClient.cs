using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Integrations.Models;

namespace TorrentBot.Integrations.Clients;

public sealed class QBittorrentClient : IQBittorrentClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string? _username;
    private readonly string? _password;
    private readonly ILogger<QBittorrentClient> _logger;
    private bool _authenticated;

    public QBittorrentClient(
        HttpClient httpClient,
        string baseUrl,
        string? username = null,
        string? password = null,
        ILogger<QBittorrentClient>? logger = null)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _username = username;
        _password = password;
        _logger = logger ?? NullLogger<QBittorrentClient>.Instance;
    }

    public async Task<IReadOnlyList<TorrentInfo>> ListTorrentsAsync(CancellationToken ct = default)
    {
        if (!await EnsureAuthenticatedAsync(ct).ConfigureAwait(false))
        {
            return [];
        }

        try
        {
            using var response = await _httpClient.GetAsync($"{_baseUrl}/api/v2/torrents/info", ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("qBittorrent list failed with status {StatusCode}", response.StatusCode);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return document.RootElement.EnumerateArray()
                .Select(item => new TorrentInfo(
                    Hash: GetPropertyOrDefault(item, "hash") ?? string.Empty,
                    Name: GetPropertyOrDefault(item, "name") ?? "unknown",
                    State: GetPropertyOrDefault(item, "state") ?? "unknown",
                    Progress: GetPropertyOrDefaultDouble(item, "progress") * 100,
                    SizeBytes: GetPropertyOrDefaultLong(item, "size"),
                    DownloadedBytes: GetPropertyOrDefaultLong(item, "downloaded"),
                    DownloadSpeed: GetPropertyOrDefaultDouble(item, "dlspeed"),
                    UploadSpeed: GetPropertyOrDefaultDouble(item, "upspeed"),
                    Category: GetPropertyOrDefault(item, "category") ?? string.Empty,
                    Paused: string.Equals(GetPropertyOrDefault(item, "state"), "pausedDL", StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "qBittorrent list failed gracefully");
            return [];
        }
    }

    public async Task<string> AddTorrentAsync(AddTorrentRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UrlOrMagnet);

        if (!await EnsureAuthenticatedAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("qBittorrent is unreachable or authentication failed.");
        }

        using var content = new MultipartFormDataContent
        {
            { new StringContent(request.UrlOrMagnet), "urls" }
        };

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            content.Add(new StringContent(request.Category), "category");
        }

        if (!string.IsNullOrWhiteSpace(request.SavePath))
        {
            content.Add(new StringContent(request.SavePath), "savepath");
        }

        using var response = await _httpClient.PostAsync($"{_baseUrl}/api/v2/torrents/add", content, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"qBittorrent add failed with status {response.StatusCode}.");
        }

        return request.UrlOrMagnet;
    }

    public async Task PauseAsync(string hash, CancellationToken ct = default)
    {
        if (!await EnsureAuthenticatedAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        await PostFormAsync("/api/v2/torrents/pause", ("hashes", hash), ct).ConfigureAwait(false);
    }

    public async Task ResumeAsync(string hash, CancellationToken ct = default)
    {
        if (!await EnsureAuthenticatedAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        await PostFormAsync("/api/v2/torrents/resume", ("hashes", hash), ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string hash, bool deleteFiles = false, CancellationToken ct = default)
    {
        if (!await EnsureAuthenticatedAsync(ct).ConfigureAwait(false))
        {
            return;
        }

        await PostFormAsync(
            "/api/v2/torrents/delete",
            [("hashes", hash), ("deleteFiles", deleteFiles ? "true" : "false")],
            ct).ConfigureAwait(false);
    }

    private async Task<bool> EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_authenticated || string.IsNullOrWhiteSpace(_username))
        {
            return true;
        }

        try
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = _username!,
                ["password"] = _password ?? string.Empty
            });

            using var response = await _httpClient.PostAsync($"{_baseUrl}/api/v2/auth/login", content, ct)
                .ConfigureAwait(false);
            _authenticated = response.IsSuccessStatusCode;
            if (!_authenticated)
            {
                _logger.LogWarning("qBittorrent authentication failed with status {StatusCode}", response.StatusCode);
            }

            return _authenticated;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "qBittorrent authentication failed gracefully");
            return false;
        }
    }

    private async Task PostFormAsync(string path, (string Key, string Value) field, CancellationToken ct) =>
        await PostFormAsync(path, [field], ct).ConfigureAwait(false);

    private async Task PostFormAsync(string path, IReadOnlyList<(string Key, string Value)> fields, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(fields.ToDictionary(f => f.Key, f => f.Value));
        using var response = await _httpClient.PostAsync($"{_baseUrl}{path}", content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("qBittorrent request to {Path} failed with status {StatusCode}", path, response.StatusCode);
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

    private static double GetPropertyOrDefaultDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : 0;
}