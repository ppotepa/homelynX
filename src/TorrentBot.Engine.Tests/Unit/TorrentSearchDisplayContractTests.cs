using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Integrations.Models;
using TorrentBot.Llm;
using TorrentBot.Plugins.Downloads;
using TorrentBot.Plugins.Torrent;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class TorrentSearchDisplayContractTests
{
    [Fact]
    public async Task Search_snapshot_prompt_and_select_round_trip_uses_1_based_display_index()
    {
        var results = new List<DownloadSearchResult>
        {
            new("torrent-0", "Alpha ISO", "torrent", 1000, 50, "magnet:a", null),
            new("torrent-1", "Beta ISO", "torrent", 900, 40, "magnet:b", null)
        };

        var context = new ConversationContext("chat-1", "admin");
        TorrentSearchConversationState.Save(context, "ubuntu", results, page: 0, pageSize: 5);

        var prompt = LlmSystemPromptBuilder.BuildPlannerPrompt(new LlmPlanningRequest(
            "wybierz pierwszy",
            [],
            [],
            Conversation: context,
            Contracts: []));

        Assert.Contains("[1] Alpha ISO", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("[0] Alpha", prompt, StringComparison.Ordinal);

        Assert.True(TorrentSearchDisplay.TrySelectGlobalIndex(1, 0, 5, results.Count, out var globalIndex));
        Assert.Equal(0, globalIndex);

        var jackett = new FakeJackettClient();
        jackett.SetResults(
        [
            new TorrentSearchResult("t1", "Alpha ISO", "magnet:1", 1000, 50, "jackett"),
            new TorrentSearchResult("t2", "Beta ISO", "magnet:2", 900, 40, "jackett")
        ]);
        var engine = EngineBootstrap.Create(downloadsPlugin: new DownloadsPlugin(jackett, new FakeQBittorrentClient()));
        await engine.StartAsync();
        try
        {
            TorrentSearchConversationState.Save(
                engine.ConversationContextStore!.GetOrCreate("chat-1", "admin"),
                "ubuntu",
                results);

            var select = await engine.SubmitAsync(new Invocation
            {
                IsExplicit = true,
                IsDryRun = true,
                CapabilityName = "torrent.select_result",
                Parameters = new Dictionary<string, object?> { ["index"] = 1 },
                RequestContext = new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "admin", source: "test", chatId: "chat-1"),
                User = new TorrentBot.Acl.AclService().ResolveUser("admin")
            });

            Assert.True(select.Success, select.Error);
            Assert.Equal("torrent-0", ExtractSelectedId(select));
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task Llm_follow_up_repair_maps_wybierz_pierwszy_to_index_1()
    {
        var context = new ConversationContext("chat-1", "admin");
        context.UpdateSnapshot("torrent_search_results", TorrentSearchConversationState.BuildSnapshot(
            "ubuntu",
            [new DownloadSearchResult("torrent-0", "Alpha ISO", "torrent", 1000, 50, "magnet:a", null)],
            page: 0,
            pageSize: 5));

        var pipeline = new LlmPipeline(new UnconfiguredLlmPlanner(), new StubLlmExecutor());
        var result = await pipeline.RunAsync(new LlmPipelineRequest(
            "wybierz pierwszy",
            [],
            Conversation: context));

        var step = Assert.Single(result.Plan.Steps);
        Assert.Equal("torrent.select_result", step.Capability);
        Assert.Equal(1, step.Parameters?["index"]);
    }

    [Fact]
    public async Task Llm_follow_up_repair_maps_wybierz_drugi_to_index_2()
    {
        var context = new ConversationContext("chat-1", "admin");
        context.UpdateSnapshot("torrent_search_results", TorrentSearchConversationState.BuildSnapshot(
            "ubuntu",
            [
                new DownloadSearchResult("torrent-0", "Alpha ISO", "torrent", 1000, 50, "magnet:a", null),
                new DownloadSearchResult("torrent-1", "Beta ISO", "torrent", 900, 40, "magnet:b", null)
            ],
            page: 0,
            pageSize: 5));

        var pipeline = new LlmPipeline(new UnconfiguredLlmPlanner(), new StubLlmExecutor());
        var result = await pipeline.RunAsync(new LlmPipelineRequest(
            "wybierz drugi",
            [],
            Conversation: context));

        var step = Assert.Single(result.Plan.Steps);
        Assert.Equal("torrent.select_result", step.Capability);
        Assert.Equal(2, step.Parameters?["index"]);
    }

    private static string? ExtractSelectedId(ExecutionResult result)
    {
        if (result.CapabilityResult?.Data is not Dictionary<string, object?> data
            || !data.TryGetValue("selected", out var selected))
        {
            return null;
        }

        return selected switch
        {
            DownloadSearchResult download => download.Id,
            _ => selected?.GetType().GetProperty("Id")?.GetValue(selected)?.ToString()
        };
    }
}