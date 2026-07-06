using TorrentBot.Contracts.Query;
using TorrentBot.Engine.Tests.Support;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class RepositoryQueryTests
{
    [Fact]
    public async Task Engine_exposes_registered_snapshot_manifests_and_query_results()
    {
        await using var scope = await EngineTestHelper.CreateStartedEngineAsync();

        var manifests = scope.Engine.GetQuerySourceManifests();
        Assert.Contains(manifests, m => m.Name == "test.items");

        var queryResult = await scope.Engine.QueryAsync(
            "test.items",
            new QuerySpec(Source: "test.items", Limit: 10));

        Assert.Equal("test.items", queryResult.Source);
        Assert.Equal(2, queryResult.Count);
        Assert.Equal("item-1", queryResult.Items[0]["id"]);
        Assert.Equal("active", queryResult.Items[0]["status"]);
    }
}