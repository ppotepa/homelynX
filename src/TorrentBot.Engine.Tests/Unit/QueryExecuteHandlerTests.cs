using TorrentBot.Contracts.Query;
using TorrentBot.Plugins.Query;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class QueryExecuteHandlerTests
{
    [Fact]
    public void ParseWhere_reads_dictionary_array_from_parameters()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["source"] = "downloads",
            ["where"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["field"] = "provider",
                    ["op"] = "eq",
                    ["value"] = "torrent"
                }
            }
        };

        var spec = QueryExecuteHandlerTestHelpers.BuildSpec(parameters, "downloads");

        Assert.NotNull(spec.Where);
        Assert.Single(spec.Where!);
        Assert.Equal("provider", spec.Where![0].Field);
        Assert.Equal("eq", spec.Where[0].Op);
        Assert.Equal("torrent", spec.Where[0].Value);
    }
}

internal static class QueryExecuteHandlerTestHelpers
{
    public static QuerySpec BuildSpec(IReadOnlyDictionary<string, object?> parameters, string source) =>
        typeof(QueryExecuteHandler)
            .GetMethod("BuildSpec", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [parameters, source]) as QuerySpec
        ?? throw new InvalidOperationException("BuildSpec returned null.");
}