using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Plugins.System.Capabilities;

public sealed class PingCapabilityHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?> { ["pong"] = true, ["traceId"] = context.Request.TraceId },
            Message: "pong",
            IsDryRun: context.IsDryRun));
}