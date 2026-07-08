using TorrentBot.Bootstrap;

namespace TorrentBot.Engine.Tests.Support;

public static class LlmTestEnvironment
{
    public static bool IsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("TORRENTBOT_RUN_LLM_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public static async Task<bool> IsLivePlannerReachableAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return false;
        }

        var url = HomelynxEnv.FirstNonEmpty(
            Environment.GetEnvironmentVariable("LLM_URL"),
            Environment.GetEnvironmentVariable("TORRENTBOT_OLLAMA_URL"),
            Environment.GetEnvironmentVariable("OLLAMA_HOST"),
            HomelynxEnv.GetServiceUrl(null, "LLM_HOST", "LLM_PORT", "LLM_HTTPS"));

        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync($"{url.TrimEnd('/')}/api/tags", ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}