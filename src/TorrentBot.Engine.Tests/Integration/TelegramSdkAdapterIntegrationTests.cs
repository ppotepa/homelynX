using System.Text.Json;
using Telegram.Bot.Types;
using TorrentBot.Acl;
using TorrentBot.Bootstrap;
using TorrentBot.Adapters.Cli;
using TorrentBot.Adapters.Telegram;
using TorrentBot.Adapters.Telegram.Sdk;
using TorrentBot.Adapters.Telegram.Verbosity;

namespace TorrentBot.Engine.Tests.Integration;

[Collection("FullStack")]
public sealed class TelegramSdkAdapterIntegrationTests
{
    [Fact]
    public void Telegram_sdk_mapper_deserializes_message_and_callback_updates()
    {
        var messageUpdate = DeserializeUpdate("""
            {
              "update_id": 1,
              "message": {
                "message_id": 10,
                "date": 0,
                "chat": { "id": 42, "type": "private" },
                "from": { "id": 7, "is_bot": false, "first_name": "Test" },
                "text": "/health"
              }
            }
            """);

        var callbackUpdate = DeserializeUpdate("""
            {
              "update_id": 2,
              "callback_query": {
                "id": "cb-1",
                "from": { "id": 7, "is_bot": false, "first_name": "Test" },
                "data": "confirm:abc123",
                "message": {
                  "message_id": 11,
                  "date": 0,
                  "chat": { "id": 42, "type": "private" }
                }
              }
            }
            """);

        var message = TelegramSdkUpdateMapper.Map(messageUpdate);
        var callback = TelegramSdkUpdateMapper.Map(callbackUpdate);

        Assert.NotNull(message);
        Assert.NotNull(callback);
        Assert.Equal("7", message!.UserId);
        Assert.Equal("7", callback!.UserId);
        Assert.True(callback.IsCallback);
    }

    [Fact]
    public async Task Telegram_production_adapter_edits_message_with_orchestrator_health_result()
    {
        var messenger = new RecordingTelegramMessenger();
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var adapter = new TelegramProductionAdapter(engine, messenger);
            await adapter.HandleMappedUpdateAsync(
                new TelegramUpdate(42, "1001", "/health", MessageId: 99),
                progressMessageId: 99);

            Assert.NotEmpty(messenger.Edited);
            Assert.Contains("healthy", messenger.Edited.Last().Text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task Telegram_production_adapter_sends_bot_owned_progress_message_for_user_commands()
    {
        var messenger = new RecordingTelegramMessenger();
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var adapter = new TelegramProductionAdapter(engine, messenger);
            var update = DeserializeUpdate("""
                {
                  "update_id": 3,
                  "message": {
                    "message_id": 55,
                    "date": 0,
                    "chat": { "id": 42, "type": "private" },
                    "from": { "id": 1001, "is_bot": false, "first_name": "Test" },
                    "text": "/health"
                  }
                }
                """);

            await adapter.HandleUpdateAsync(update);

            Assert.Contains(messenger.Sent, msg => msg.Text == "Working...");
            Assert.NotEmpty(messenger.Edited);
            Assert.DoesNotContain(messenger.Edited, edit => edit.MessageId == 55);
            Assert.Contains("healthy", messenger.Edited.Last().Text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task Telegram_production_adapter_returns_acl_denial_to_user()
    {
        var messenger = new RecordingTelegramMessenger();
        var acl = new AclService(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["1001"] = "PUBLIC"
        });
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var adapter = new TelegramProductionAdapter(engine, messenger, acl);
            var response = await adapter.HandleMappedUpdateAsync(
                new TelegramUpdate(42, "1001", "/cancel job:missing"),
                progressMessageId: 200);

            Assert.Contains("denied", response, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(messenger.Edited, edit =>
                edit.Text.Contains("denied", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Theory]
    [InlineData("off", 1, null)]
    [InlineData("low", 2, "parse:")]
    [InlineData("medium", 3, "plan:")]
    [InlineData("full", 3, "execute:")]
    public async Task Telegram_production_adapter_applies_verbosity_levels_for_in_place_edits(
        string level,
        int expectedEdits,
        string? expectedStagePrefix)
    {
        var messenger = new RecordingTelegramMessenger();
        var verbosity = new VerbositySettingsStore();
        var engine = EngineBootstrap.Create();
        await engine.StartAsync();
        try
        {
            var adapter = new TelegramProductionAdapter(engine, messenger, verbositySettings: verbosity);
            await adapter.HandleMappedUpdateAsync(
                new TelegramUpdate(42, "1001", $"verbosity {level}", MessageId: 1),
                progressMessageId: 1);

            messenger.Edited.Clear();
            await adapter.HandleMappedUpdateAsync(
                new TelegramUpdate(42, "1001", "/health", MessageId: 2),
                progressMessageId: 2);

            Assert.Equal(expectedEdits, messenger.Edited.Count);
            if (expectedStagePrefix is not null)
            {
                Assert.Contains(messenger.Edited, edit =>
                    edit.Text.Contains(expectedStagePrefix, StringComparison.OrdinalIgnoreCase));
            }

            if (expectedEdits > 0)
            {
                Assert.Contains("healthy", messenger.Edited.Last().Text, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    private static Update DeserializeUpdate(string json) =>
        JsonSerializer.Deserialize<Update>(json, JsonOptions()) ?? throw new InvalidOperationException("Update deserialization failed.");

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}