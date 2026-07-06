using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Plugins;
using TorrentBot.Contracts.Repositories;
using TorrentBot.Engine.Capabilities;
using TorrentBot.Engine.Repositories;

namespace TorrentBot.Engine.Plugins;

public sealed class PluginRegistrationContext : IPluginRegistrationContext
{
    private readonly CapabilityRegistry _capabilities;
    private readonly RepositoryAggregator _repositories;
    private readonly Dictionary<Type, object> _services = new();

    public PluginRegistrationContext(CapabilityRegistry capabilities, RepositoryAggregator repositories)
    {
        _capabilities = capabilities;
        _repositories = repositories;
    }

    public void RegisterCapability(CapabilityMetadata metadata, ICapabilityHandler handler) =>
        _capabilities.Register(metadata, handler);

    public void RegisterSnapshotSource(ISnapshotSource source) =>
        _repositories.Register(source);

    public void RegisterService<T>(T service) where T : class
    {
        ArgumentNullException.ThrowIfNull(service);
        _services[typeof(T)] = service;
    }

    public T? GetService<T>() where T : class =>
        _services.TryGetValue(typeof(T), out var service) ? (T)service : default;

    public object? GetService(Type serviceType) =>
        _services.TryGetValue(serviceType, out var service) ? service : null;

    internal IReadOnlyDictionary<Type, object> GetRegisteredServices() => _services;
}