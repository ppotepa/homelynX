using TorrentBot.Contracts.Conversation;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class IndexSelectionParsingTests
{
    [Theory]
    [InlineData("wybierz drugi", 2)]
    [InlineData("select 3", 3)]
    [InlineData("/select 2", 2)]
    [InlineData("2", 2)]
    [InlineData("wybierz pierwszy", 1)]
    [InlineData("pick the second", 2)]
    public void TryParseDisplayIndex_parses_natural_language_and_commands(string text, int expected)
    {
        Assert.True(IndexSelectionParsing.TryParseDisplayIndex(text, out var index));
        Assert.Equal(expected, index);
    }

    [Theory]
    [InlineData("szukaj ubuntu")]
    [InlineData("pokaż status")]
    [InlineData("")]
    public void TryParseDisplayIndex_returns_false_for_non_selection_utterances(string text)
    {
        Assert.False(IndexSelectionParsing.TryParseDisplayIndex(text, out _));
    }

    [Fact]
    public void LooksLikeIndexSelection_matches_keywords_without_number()
    {
        Assert.True(IndexSelectionParsing.LooksLikeIndexSelection("wybierz coś"));
        Assert.False(IndexSelectionParsing.LooksLikeIndexSelection("szukaj ubuntu"));
    }
}