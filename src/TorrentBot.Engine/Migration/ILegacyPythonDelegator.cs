using System.Net.Http.Json;
using TorrentBot.Contracts.Invocation;

namespace TorrentBot.Engine.Migration;

public interface ILegacyPythonDelegator
{
    bool IsEnabled { get; }
    Task<ExecutionResult?> TryDelegateAsync(Invocation invocation, CancellationToken cancellationToken = default);
}

public sealed class NoOpLegacyPythonDelegator : ILegacyPythonDelegator
{
    public bool IsEnabled => false;

    public Task<ExecutionResult?> TryDelegateAsync(Invocation invocation, CancellationToken cancellationToken = default) =>
        Task.FromResult<ExecutionResult?>(null);
}

public sealed class HttpLegacyPythonDelegator : ILegacyPythonDelegator
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public HttpLegacyPythonDelegator(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public bool IsEnabled => FeatureFlags.FromEnvironment().EnableLegacyPythonShim;

    public async Task<ExecutionResult?> TryDelegateAsync(Invocation invocation, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var payload = new
        {
            capability = invocation.CapabilityName ?? invocation.Command,
            text = invocation.Text,
            userId = invocation.User.UserId,
            parameters = invocation.Parameters,
            isDryRun = invocation.IsDryRun
        };

        using var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/delegate", payload, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ExecutionResult(Success: false, Error: $"Legacy Python delegation failed: {(int)response.StatusCode}");
        }

        var body = await response.Content.ReadFromJsonAsync<LegacyDelegateResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new ExecutionResult(
            Success: body?.Success ?? false,
            Error: body?.Error,
            CapabilityResult: body?.Message is null
                ? null
                : new TorrentBot.Contracts.Capabilities.CapabilityResult(body.Success, Message: body.Message));
    }

    private sealed record LegacyDelegateResponse(bool Success, string? Message, string? Error);
}