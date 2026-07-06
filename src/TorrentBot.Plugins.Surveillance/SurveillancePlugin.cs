using TorrentBot.Contracts.Plugins;
using TorrentBot.Integrations.Fakes;
using TorrentBot.Integrations.Interfaces;
using TorrentBot.Plugins.Surveillance.Capabilities;
using TorrentBot.Plugins.Surveillance.Snapshots;

namespace TorrentBot.Plugins.Surveillance;

public sealed class SurveillancePlugin : IPlugin
{
    private readonly ISurveillanceClient _client;

    public SurveillancePlugin(ISurveillanceClient? client = null) => _client = client ?? new FakeSurveillanceClient();

    public string Name => "surveillance";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        context.RegisterService<ISurveillanceClient>(_client);
        foreach (var metadata in SurveillanceCapabilities.All)
        {
            context.RegisterCapability(metadata, new SurveillanceCapabilityHandler(metadata.Name));
        }

        context.RegisterSnapshotSource(new SurveillanceEventsSnapshotSource(_client));
    }
}