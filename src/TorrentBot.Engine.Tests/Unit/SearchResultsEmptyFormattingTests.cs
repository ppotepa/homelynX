using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Engine.Pipeline.ResponseArtifacts;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class SearchResultsEmptyFormattingTests
{
    [Fact]
    public void Parser_builds_search_artifact_when_zero_results()
    {
        var data = new Dictionary<string, object?>
        {
            ["query"] = "ubuntu iso 22",
            ["totalCount"] = 0,
            ["count"] = 0,
            ["page"] = 0,
            ["pageSize"] = 5,
            ["hasMore"] = false,
            ["totalPages"] = 1,
            ["results"] = Array.Empty<Dictionary<string, object?>>()
        };
        var spec = new ResponseConstructionSpec("search_results", ItemsKey: "results", QueryKey: "query");

        Assert.True(SearchResultsArtifactParser.TryParse(data, out var artifact, spec));
        Assert.Equal(0, artifact.TotalCount);

        var text = SearchResultsFormatting.FormatTelegram(artifact);
        Assert.Contains("Brak wynikow", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ubuntu iso 22", text, StringComparison.Ordinal);
    }
}