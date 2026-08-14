using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class SearchResultsTelegramButtonsTests
{
    [Fact]
    public void Render_Telegram_builds_button_per_item_on_page()
    {
        var items = Enumerable.Range(1, 5)
            .Select(i => new SearchResultItem(i, $"Item {i}", 1_000_000, 10, null, null, "torrent"))
            .ToList();

        var artifact = new SearchResultsArtifact("ubuntu 22", 5, 0, 5, items, false, 1);
        var rendered = SearchResultsFormatting.Render(artifact, new RenderContext(RenderChannel.Telegram));

        Assert.Equal(5, rendered.Buttons!.Count);
        Assert.All(Enumerable.Range(1, 5), i =>
            Assert.Contains(rendered.Buttons, b => b.CallbackData == $"select:{i}"));
    }
}