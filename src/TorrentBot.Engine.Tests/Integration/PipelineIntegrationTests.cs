using TorrentBot.Bootstrap;
using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Models;
using TorrentBot.Presentation;
using TorrentBot.Plugins.Downloads;

namespace TorrentBot.Engine.Tests.Integration;

[Collection("FullStack")]
public sealed class PipelineIntegrationTests
{
    [Fact]
    public async Task Pipeline_search_renders_paginated_telegram_output()
    {
        var jackett = new FakeJackettClient();
        jackett.SetResults(
        [
            new TorrentSearchResult("t1", "Ubuntu 24.04", "magnet:1", 4_000_000_000, 50, "jackett"),
            new TorrentSearchResult("t2", "Ubuntu 22.04", "magnet:2", 3_000_000_000, 40, "jackett"),
            new TorrentSearchResult("t3", "Debian 12", "magnet:3", 2_000_000_000, 30, "jackett"),
            new TorrentSearchResult("t4", "Fedora 39", "magnet:4", 1_000_000_000, 25, "jackett"),
            new TorrentSearchResult("t5", "Mint 21", "magnet:5", 900_000_000, 20, "jackett"),
            new TorrentSearchResult("t6", "Arch ISO", "magnet:6", 800_000_000, 15, "jackett")
        ]);
        var engine = EngineBootstrap.Create(downloadsPlugin: new DownloadsPlugin(jackett, new FakeQBittorrentClient()));
        await engine.StartAsync();
        try
        {
            var pipeline = PipelineBootstrap.Create(engine, engine.LlmPipeline);
            var result = await pipeline.RunAsync(new TorrentBot.Contracts.Invocation.Invocation
            {
                IsExplicit = true,
                CapabilityName = "torrent.search",
                Parameters = new Dictionary<string, object?> { ["query"] = "ubuntu" },
                RequestContext = new TorrentBot.Contracts.Context.RequestContext("t", "i", "admin", source: "test"),
                User = new TorrentBot.Acl.AclService().ResolveUser("admin")
            });

            Assert.True(result.Success);
            var search = Assert.IsType<SearchResultsArtifact>(result.Artifacts.Items[0]);
            Assert.Equal(6, search.TotalCount);
            Assert.Equal(5, search.Items.Count);
            Assert.True(search.HasMore);

            var rendered = PresentationBootstrap.CreateDefault().Render(
                result.Artifacts,
                new RenderContext(RenderChannel.Telegram));
            Assert.Contains("Wyniki:", rendered.Text);
            Assert.Contains("/select", rendered.Text);
            Assert.Contains("/more", rendered.Text);
            Assert.NotNull(rendered.Buttons);
        }
        finally
        {
            await engine.StopAsync();
        }
    }
}