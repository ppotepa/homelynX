using TorrentBot.Acl;
using TorrentBot.Bootstrap;
using TorrentBot.Adapters.Cli;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Integrations.Fakes;

namespace TorrentBot.Engine.Tests.Integration;

public sealed class TtsMediaIntegrationTests
{
    [Fact]
    public async Task Tts_say_capability_calls_http_edge_client()
    {
        var tts = new FakeTtsClient();
        var engine = EngineBootstrap.Create(mediaPlugin: new TorrentBot.Plugins.Media.MediaPlugin(tts));
        await engine.StartAsync();
        try
        {
        var result = await engine.SubmitAsync(new Invocation
        {
            IsExplicit = true,
            CapabilityName = "tts.say",
            Parameters = new Dictionary<string, object?> { ["text"] = "hello world" },
            RequestContext = new RequestContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "admin", source: "test"),
            User = new AclService().ResolveUser("admin")
        });

        Assert.True(result.Success, result.Error);
        var data = Assert.IsType<Dictionary<string, object?>>(result.CapabilityResult!.Data);
        Assert.Equal("tts-http", data["service"]);
        Assert.Contains("audio", data["audio_url"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await engine.StopAsync();
        }
    }
}