using TorrentBot.Contracts.Context;

namespace TorrentBot.Contracts.Capabilities;

public interface ICapabilityHandler
{
    Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken);
}