using TorrentBot.Bootstrap;
using TorrentBot.Adapters.Cli;
using TorrentBot.Engine.Tests.Support;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Models;
using TorrentBot.Llm;
using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Engine.Tests.Integration;

[CollectionDefinition("CliApplication", DisableParallelization = true)]
public sealed class CliApplicationCollection
{
}

[Collection("CliApplication")]
public sealed class CliApplicationTests
{
    [Fact]
    public async Task Cli_capability_call_system_health_exits_zero_with_json()
    {
        var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            var exitCode = await CliApplication.RunAsync(["capability", "call", "system.health", "--json"]);
            Assert.Equal(0, exitCode);
            Assert.Contains("healthy", writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task Cli_capabilities_list_exits_zero()
    {
        var exitCode = await CliApplication.RunAsync(["capabilities", "list", "--json"]);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Cli_query_downloads_exits_zero_with_json_items()
    {
        var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            var jackett = new FakeJackettClient();
            jackett.SetResults([new TorrentSearchResult("t1", "Ubuntu", "magnet:1", 1000, 10, "jackett")]);
            var qbit = new FakeQBittorrentClient();
            await qbit.AddTorrentAsync(new AddTorrentRequest("magnet:?xt=urn:btih:ubuntu"));

            var exitCode = await CliApplication.RunAsync(
                ["query", "downloads", "--json", "--user", "admin"],
                () => EngineBootstrap.Create(downloadsPlugin: new DownloadsPlugin(jackett, qbit)));

            Assert.Equal(0, exitCode);
            Assert.Contains("items", writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task Cli_query_downloads_where_filter_applies_status_predicate()
    {
        var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            var qbit = new FakeQBittorrentClient();
            var activeHash = await qbit.AddTorrentAsync(new AddTorrentRequest("magnet:?xt=urn:btih:active"));
            var pausedHash = await qbit.AddTorrentAsync(new AddTorrentRequest("magnet:?xt=urn:btih:paused"));
            await qbit.PauseAsync(pausedHash);

            var exitCode = await CliApplication.RunAsync(
                ["query", "downloads", "--where", "status=eq:downloading", "--json", "--user", "admin"],
                () => EngineBootstrap.Create(downloadsPlugin: new DownloadsPlugin(qBittorrent: qbit)));

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains(activeHash, output);
            Assert.DoesNotContain(pausedHash, output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Cli_try_parse_where_expression_supports_field_eq_op_value()
    {
        Assert.True(CliApplication.TryParseWhereExpression("status=eq:downloading", out var clause));
        Assert.Equal("status", clause["field"]);
        Assert.Equal("eq", clause["op"]);
        Assert.Equal("downloading", clause["value"]);
    }

    [Fact]
    public async Task Cli_agent_plan_active_downloads_executes_via_orchestrator()
    {
        var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            var exitCode = await CliApplication.RunAsync(
                ["agent", "plan", "are there any active downloads?", "--json", "--dry-run", "--user", "admin"],
                () => EngineBootstrap.Create(
                    llmPipeline: new LlmPipeline(
                        FixedPlanLlmPlanner.ActiveDownloads(),
                        new StubLlmExecutor())));

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("downloads", output);
            Assert.Contains("\"Success\": true", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}