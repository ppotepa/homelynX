using TorrentBot.Contracts.Bus;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Engine.Bus;

public sealed class InMemoryBus : IInternalBus
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public void Publish<T>(T message, IRequestContext context) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);

        var correlated = new CorrelatedMessage<T>
        {
            Payload = message,
            Context = context
        };

        List<Delegate> snapshot;
        lock (_gate)
        {
            snapshot = _handlers.TryGetValue(typeof(T), out var list)
                ? list.ToList()
                : [];
        }

        foreach (var handler in snapshot)
        {
            ((Action<CorrelatedMessage<T>>)handler)(correlated);
        }
    }

    public IDisposable Subscribe<T>(Action<CorrelatedMessage<T>> handler) where T : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = [];
                _handlers[typeof(T)] = list;
            }

            list.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(typeof(T), out var list))
                {
                    list.Remove(handler);
                }
            }
        });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            dispose();
        }
    }
}