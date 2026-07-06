using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Contracts.Plugins;

public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    void Register(IPluginRegistrationContext context);
}

public interface IPluginRegistrationContext
{
    void RegisterCapability(CapabilityMetadata metadata, ICapabilityHandler handler);
    void RegisterSnapshotSource(ISnapshotSource source);
    void RegisterService<T>(T service) where T : class;
}