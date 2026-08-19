using TorrentBot.Adapters.Telegram;
using TorrentBot.Adapters.Telegram.Sdk;
using TorrentBot.Bootstrap;
using TorrentBot.Engine;

namespace TorrentBot.Engine.Tests.Integration;

[Collection("FullStack")]
public sealed class ToolsFeatureIntegrationTests
{
    [Fact]
    public async Task Chiptune_is_rendered_and_delivered_as_audio()
    {
        var (engine, messenger, restore) = Create();
        try
        {
            await engine.StartAsync();
            var adapter = new TelegramProductionAdapter(engine, messenger);
            var response = await adapter.HandleMappedUpdateAsync(new TelegramUpdate(42, "feature-user", "/chiptune notes=\"C4/8 E4/8 G4/4\" format=wav", 20), 20);
            Assert.Contains("Chiptune generated", response);
            Assert.Contains(messenger.Audios, x => x.FileName == "chiptune.wav" && x.Size > 44);
        }
        finally { await engine.StopAsync(); restore(); }
    }

    [Fact]
    public async Task Midi_attachment_is_downloaded_and_rendered_as_audio()
    {
        var (engine, messenger, restore) = Create();
        try
        {
            messenger.Downloads["midi-file"] =
            [
                (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6, 0, 0, 0, 1, 1, 0xE0,
                (byte)'M', (byte)'T', (byte)'r', (byte)'k', 0, 0, 0, 13,
                0, 0x90, 60, 100, 0x83, 0x60, 0x80, 60, 0, 0, 0xFF, 0x2F, 0
            ];
            await engine.StartAsync();
            var adapter = new TelegramProductionAdapter(engine, messenger);
            var update = new TelegramUpdate(42, "feature-user", "/chiptune format=wav", 26, Attachment: new TelegramAttachment("midi-file", "song.mid", "audio/midi"));
            var response = await adapter.HandleMappedUpdateAsync(update, 26);
            Assert.Contains("Chiptune generated", response);
            Assert.Contains(messenger.Audios, x => x.FileName == "chiptune.wav" && x.Size > 44);
        }
        finally { await engine.StopAsync(); restore(); }
    }

    [Fact]
    public async Task Chiptune_composer_callback_loads_persisted_spec_and_renders_variation()
    {
        var (engine,messenger,restore)=Create();
        try
        {
            await engine.StartAsync();var adapter=new TelegramProductionAdapter(engine,messenger);
            await adapter.HandleMappedUpdateAsync(new TelegramUpdate(42,"feature-user","/chiptune generate=riff bars=1 seed=7 format=wav",30),30);
            var button=messenger.Sent.SelectMany(x=>x.Buttons??[]).Single(x=>x.Text=="Variation");
            var response=await adapter.HandleMappedUpdateAsync(new TelegramUpdate(42,"feature-user",null,31,button.CallbackData),31);
            Assert.Contains("seed=8",response);
            Assert.Equal(2,messenger.Audios.Count);
        }
        finally{await engine.StopAsync();restore();}
    }

    [Fact]
    public async Task Location_tracking_and_map_commands_use_the_private_feature_store()
    {
        var (engine, messenger, restore) = Create();
        try
        {
            await engine.StartAsync();
            var adapter = new TelegramProductionAdapter(engine, messenger);
            Assert.Contains("Home saved", await adapter.HandleMappedUpdateAsync(new TelegramUpdate(42, "feature-user", "/home set 52.2297 21.0122", 21), 21));
            Assert.Contains("Distance:", await adapter.HandleMappedUpdateAsync(new TelegramUpdate(42, "feature-user", "/distance home 50.0614 19.9383", 22), 22));
            Assert.Contains("Map generated", await adapter.HandleMappedUpdateAsync(new TelegramUpdate(42, "feature-user", "/map home", 23), 23));
            Assert.True(messenger.Documents.Any(x => (x.FileName is "map.svg" or "map.png") && x.Size > 100) || messenger.Photos.Any(x => x.FileName == "map.png" && x.Size > 100));
            Assert.Contains("Tracking #", await adapter.HandleMappedUpdateAsync(new TelegramUpdate(42, "feature-user", "/track add RR123456789PL label=Parcel", 24), 24));
            Assert.Contains("RR123456789PL", await adapter.HandleMappedUpdateAsync(new TelegramUpdate(42, "feature-user", "/track list", 25), 25));
        }
        finally { await engine.StopAsync(); restore(); }
    }

    private static (EngineHost Engine, RecordingTelegramMessenger Messenger, Action Restore) Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "homelynx-feature-tests", Guid.NewGuid().ToString("N"), "tools.db");
        var previous = Environment.GetEnvironmentVariable("TORRENTBOT_TOOLS_DB");
        Environment.SetEnvironmentVariable("TORRENTBOT_TOOLS_DB", path);
        return (EngineBootstrap.Create(), new RecordingTelegramMessenger(), () => Environment.SetEnvironmentVariable("TORRENTBOT_TOOLS_DB", previous));
    }
}
