using TorrentBot.Adapters.Telegram;
using TorrentBot.Adapters.Telegram.Sdk;
using TorrentBot.Bootstrap;

namespace TorrentBot.Adapters.Telegram.Host;

public static class TelegramHostHarness
{
    public static async Task<int> RunAsync()
    {
        Console.WriteLine("TelegramHostHarness: entering via Program.Main path");
        var messenger = new RecordingTelegramMessenger();
        var engine = EngineBootstrap.Create();
        await engine.StartAsync().ConfigureAwait(false);
        try
        {
            var adapter = new TelegramProductionAdapter(engine, messenger);
            for (var i = 0; i < 2; i++)
            {
                var reply = await adapter.HandleMappedUpdateAsync(
                    new TelegramUpdate(42, "harness-user", "/health", MessageId: 100 + i),
                    progressMessageId: 100 + i).ConfigureAwait(false);
                Console.WriteLine($"HARNESS_RUN_{i + 1}: {reply}");
                if (string.IsNullOrWhiteSpace(reply))
                {
                    Console.WriteLine("TelegramHostHarness: empty reply — exit=1");
                    return 1;
                }
            }

            Console.WriteLine("TelegramHostHarness: success exit=0");
            return 0;
        }
        finally
        {
            await engine.StopAsync().ConfigureAwait(false);
        }
    }
}