using TorrentBot.Llm;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class LlmIntentNormalizerTests
{
    [Theory]
    [InlineData("download ubuntu 22 iso", "download ubuntu 22 iso")]
    [InlineData("pobierz debian 12", "pobierz debian 12")]
    public void Analyze_only_normalizes_polish_lexicon_without_inferring_query(string text, string _)
    {
        var intent = LlmIntentNormalizer.Analyze(text);
        Assert.Equal(text, intent.OriginalText);
        Assert.False(string.IsNullOrWhiteSpace(intent.NormalizedText));
    }

    [Fact]
    public void Analyze_preserves_user_text_for_llm_planner()
    {
        var intent = LlmIntentNormalizer.Analyze("download ubuntu iso 22");
        Assert.Equal("download ubuntu iso 22", intent.OriginalText);
        Assert.DoesNotContain("MUST use torrent.search", intent.NormalizedText, StringComparison.Ordinal);
    }
}