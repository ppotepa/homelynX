using TorrentBot.Contracts.Bus;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Engine.Bus;

public interface IInternalBus
{
    void Publish<T>(T message, IRequestContext context) where T : class;
    IDisposable Subscribe<T>(Action<CorrelatedMessage<T>> handler) where T : class;
}