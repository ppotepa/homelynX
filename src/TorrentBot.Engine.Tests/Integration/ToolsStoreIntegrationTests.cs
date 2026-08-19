using TorrentBot.Plugins.Tools;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class ToolsStoreIntegrationTests
{
    [Fact]
    public async Task Short_links_count_visits_until_the_configured_limit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "homelynx-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ToolsStore(Path.Combine(directory, "tools.db"));
            await store.CreateShortLink("user-1", "demo", "https://example.com", "Example", "test", null, 1);

            var first = await store.ResolveShortLink("demo", countVisit: true);
            var second = await store.ResolveShortLink("demo", countVisit: true);

            Assert.NotNull(first);
            Assert.Equal(1, first!.Visits);
            Assert.NotNull(second);
            Assert.Equal(1, second!.Visits);
            Assert.False(second.VisitAccepted);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Short_links_can_be_disabled_without_deleting_the_record()
    {
        var directory = Path.Combine(Path.GetTempPath(), "homelynx-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ToolsStore(Path.Combine(directory, "tools.db"));
            await store.CreateShortLink("user-1", "demo", "https://example.com", "Example", "", null, null);
            await store.DisableShortLink("user-1", "demo");

            var link = await store.ResolveShortLink("demo", countVisit: true);

            Assert.NotNull(link);
            Assert.True(link!.Disabled);
            Assert.Equal(0, link.Visits);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
