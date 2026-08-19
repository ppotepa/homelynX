using TorrentBot.Adapters.Telegram;
using TorrentBot.Adapters.Telegram.Sdk;
using TorrentBot.Adapters.Telegram.Verbosity;
using TorrentBot.Bootstrap;

namespace TorrentBot.Engine.Tests.Integration;

[Collection("FullStack")]
public sealed class VerboseDownloadReproTests
{
    [Fact]
    public async Task Verbose_full_download_ubuntu_22_iso_records_planning_stages()
    {
        var messenger = new RecordingTelegramMessenger();
        var verbosity = new VerbositySettingsStore();
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var adapter = new TelegramProductionAdapter(engine, messenger, verbositySettings: verbosity);
            await adapter.HandleMappedUpdateAsync(
                new TelegramUpdate(42, "1001", "verbosity full", MessageId: 1),
                progressMessageId: 1);

            messenger.Edited.Clear();
            var response = await adapter.HandleMappedUpdateAsync(
                new TelegramUpdate(42, "1001", "/search ubuntu 22 iso", MessageId: 2),
                progressMessageId: 2);

            Assert.Contains("ubuntu", response, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Wyniki:", response, StringComparison.Ordinal);
            Assert.Contains(messenger.Edited, edit =>
                edit.Text.Contains("torrent.search", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(messenger.Edited, edit =>
                edit.Text.Contains("torrent.search", StringComparison.OrdinalIgnoreCase)
                || edit.Text.Contains("Planowanie", StringComparison.OrdinalIgnoreCase)
                || edit.Text.Contains("Zaplanowano", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await engine.StopAsync();
        }
    }
}
