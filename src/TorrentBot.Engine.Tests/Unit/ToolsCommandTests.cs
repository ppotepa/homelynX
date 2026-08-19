using TorrentBot.Bootstrap;
using TorrentBot.Engine;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ToolsCommandTests
{
    [Fact]
    public void Slash_router_preserves_tool_payloads()
    {
        Assert.Equal("add hello", SlashCommandRouting.ParseParameters("/note", "add hello")!["text"]);
        Assert.Equal("question | yes | no", SlashCommandRouting.ParseParameters("/poll", "question | yes | no")!["text"]);
        Assert.Equal("10 km mi", SlashCommandRouting.ParseParameters("/convert", "10 km mi")!["text"]);
    }

    [Fact]
    public async Task Bootstrap_registers_the_complete_tools_surface()
    {
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var names = engine.GetCapabilityContracts().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("tools.note", names);
            Assert.Contains("tools.todo", names);
            Assert.Contains("tools.trash", names);
            Assert.Contains("tools.webhook", names);
            Assert.Contains("tools.translate", names);
            Assert.Contains("tools.extract_tasks", names);
            Assert.Contains("tools.barcode", names);
            Assert.Contains("tools.shorten", names);
            Assert.Contains("tools.thumbnail", names);
            Assert.Contains("tools.compress", names);
            Assert.Contains("tools.chiptune", names);
            Assert.Contains("tools.read", names);
            Assert.Contains("tools.screenshot", names);
            Assert.Contains("tools.track", names);
            Assert.Contains("tools.home", names);
            Assert.Contains("tools.distance", names);
            Assert.Contains("tools.map", names);
        }
        finally
        {
            await engine.StopAsync();
        }
    }
}
