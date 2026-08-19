using TorrentBot.Adapters.Telegram;
using TorrentBot.Adapters.Telegram.Sdk;
using TorrentBot.Bootstrap;

namespace TorrentBot.Engine.Tests.Integration;

[Collection("FullStack")]
public sealed class ToolsArtifactIntegrationTests
{
    [Fact]
    public async Task Telegram_qr_command_delivers_a_local_photo()
    {
        var messenger = new RecordingTelegramMessenger();
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var adapter = new TelegramProductionAdapter(engine, messenger);
            var response = await adapter.HandleMappedUpdateAsync(new TelegramUpdate(42, "1001", "/qr url https://example.com", 10), 10);
            Assert.Contains("QR", response, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(messenger.Photos, photo => photo.FileName == "qr.png" && photo.Size > 100);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task Telegram_barcode_command_delivers_a_document()
    {
        var messenger = new RecordingTelegramMessenger();
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var adapter = new TelegramProductionAdapter(engine, messenger);
            var response = await adapter.HandleMappedUpdateAsync(new TelegramUpdate(42, "1001", "/barcode code128 HOMELYNX-42", 11), 11);
            Assert.Contains("barcode", response, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(messenger.Documents, document => document.FileName == "barcode.svg" && document.Size > 100);
        }
        finally
        {
            await engine.StopAsync();
        }
    }
}
