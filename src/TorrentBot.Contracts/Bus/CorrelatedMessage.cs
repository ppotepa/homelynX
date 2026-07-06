using TorrentBot.Contracts.Context;

namespace TorrentBot.Contracts.Bus;

public interface ICorrelatedEvent
{
    IRequestContext Context { get; }
}

public sealed class CorrelatedMessage<T> where T : class
{
    public required T Payload { get; init; }
    public required IRequestContext Context { get; init; }
}