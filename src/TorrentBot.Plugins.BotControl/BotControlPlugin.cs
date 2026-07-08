using TorrentBot.Contracts.Plugins;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Plugins.BotControl.Capabilities;

namespace TorrentBot.Plugins.BotControl;

public sealed class BotControlPlugin : IPlugin
{
    public string Name => "bot_control";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        context.RegisterCapability(BotControlCapabilities.DiagMetadata, new BotDiagHandler());
        context.RegisterCapability(BotControlCapabilities.PluginsMetadata, new BotPluginsHandler());
        context.RegisterCapability(BotControlCapabilities.PluginsReloadMetadata, new BotPluginsReloadHandler());
    }
}