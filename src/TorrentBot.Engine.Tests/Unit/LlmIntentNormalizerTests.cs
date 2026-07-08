using TorrentBot.Llm;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class LlmIntentNormalizerTests
{
    [Theory]
    [InlineData("download ubuntu 22 iso", "ubuntu 22 iso")]
    [InlineData("download ubuntu iso 22", "ubuntu iso 22")]
    [InlineData("pobierz debian 12", "debian 12")]
    [InlineData("get linux mint", "linux mint")]
    public void Analyze_maps_download_intent_to_forced_search_query(string text, string expectedQuery)
    {
        var intent = LlmIntentNormalizer.Analyze(text);
        Assert.Equal(expectedQuery, intent.ForcedSearchQuery);
        Assert.Contains("torrent.search", intent.NormalizedText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("download list")]
    [InlineData("pokaż pobierania")]
    [InlineData("status pobierania")]
    public void Analyze_does_not_treat_status_commands_as_download_search(string text)
    {
        var intent = LlmIntentNormalizer.Analyze(text);
        Assert.Null(intent.ForcedSearchQuery);
    }
}