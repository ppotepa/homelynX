using TorrentBot.Contracts.Query;
using TorrentBot.Contracts.Repositories;
using TorrentBot.Query;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class DuckDbQueryCompilerTests
{
    private static readonly QuerySourceMeta Meta = new(
        "downloads",
        "downloads",
        [
            new QueryFieldMeta("status", "string"),
            new QueryFieldMeta("name", "string"),
            new QueryFieldMeta("provider", "string"),
            new QueryFieldMeta("progress", "number")
        ]);

    [Fact]
    public void Execute_filters_rows_with_eq_operator()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["status"] = "downloading", ["name"] = "a" },
            new() { ["status"] = "completed", ["name"] = "b" }
        };

        var engine = new DuckDbQueryEngine();
        var result = engine.Execute(Meta, rows, new QuerySpec(
            "downloads",
            [new QueryWhere("status", "eq", "downloading")],
            ["status", "name"],
            Limit: 10));

        Assert.Single(result.Items);
        Assert.Equal("downloading", result.Items[0]["status"]);
    }

    [Fact]
    public void Execute_filters_rows_with_not_in_operator()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["status"] = "downloading", ["name"] = "a" },
            new() { ["status"] = "completed", ["name"] = "b" },
            new() { ["status"] = "failed", ["name"] = "c" }
        };

        var engine = new DuckDbQueryEngine();
        var result = engine.Execute(Meta, rows, new QuerySpec(
            "downloads",
            [new QueryWhere("status", "not_in", new object[] { "completed", "failed" })],
            ["status", "name"],
            Limit: 10));

        Assert.Single(result.Items);
        Assert.Equal("downloading", result.Items[0]["status"]);
    }

    [Fact]
    public void Execute_filters_rows_with_between_operator()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["progress"] = 0.1, ["name"] = "a" },
            new() { ["progress"] = 0.5, ["name"] = "b" },
            new() { ["progress"] = 0.9, ["name"] = "c" }
        };

        var engine = new DuckDbQueryEngine();
        var result = engine.Execute(Meta, rows, new QuerySpec(
            "downloads",
            [new QueryWhere("progress", "between", new object[] { 0.2, 0.8 })],
            ["progress", "name"],
            Limit: 10));

        Assert.Single(result.Items);
        Assert.Equal("b", result.Items[0]["name"]);
    }

    [Fact]
    public void Execute_filters_rows_with_fuzzy_contains_operator()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "Ubuntu-24.04-LTS", ["status"] = "downloading" },
            new() { ["name"] = "Debian Stable", ["status"] = "completed" }
        };

        var engine = new DuckDbQueryEngine();
        var result = engine.Execute(Meta, rows, new QuerySpec(
            "downloads",
            [new QueryWhere("name", "fuzzy_contains", "ubuntu2404lts")],
            ["name", "status"],
            Limit: 10));

        Assert.Single(result.Items);
        Assert.Contains("Ubuntu", result.Items[0]["name"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}