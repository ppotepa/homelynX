using System.Threading.Channels;
using TorrentBot.Contracts.Bus;
using TorrentBot.Contracts.Context;

namespace TorrentBot.Engine.Bus;

public sealed class QueuedEventBus : IInternalBus, IAsyncDisposable
{
    private readonly Channel<(object Message, IRequestContext Context)> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processor;
    private readonly object _gate = new();
    private readonly Dictionary<Type, List<Action<object, IRequestContext>>> _handlers = new();

    public QueuedEventBus(int capacity = 256)
    {
        _channel = Channel.CreateBounded<(object, IRequestContext)>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        _processor = Task.Run(ProcessAsync);
    }

    public void Publish<T>(T message, IRequestContext context) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        if (!_channel.Writer.TryWrite((message, context)))
        {
            throw new InvalidOperationException("Event queue is full or closed.");
        }
    }

    public IDisposable Subscribe<T>(Action<CorrelatedMessage<T>> handler) where T : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        Action<object, IRequestContext> wrapper = (message, context) =>
            handler(new CorrelatedMessage<T> { Payload = (T)message, Context = context });

        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = [];
                _handlers[typeof(T)] = list;
            }

            list.Add(wrapper);
        }

        return new Subscription(() =>
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(typeof(T), out var list))
                {
                    list.Remove(wrapper);
                }
            }
        });
    }

    private void Dispatch(object message, IRequestContext context)
    {
        List<Action<object, IRequestContext>> snapshot;
        lock (_gate)
        {
            snapshot = _handlers.TryGetValue(message.GetType(), out var list) ? list.ToList() : [];
        }

        foreach (var handler in snapshot)
        {
            handler(message, context);
        }
    }

    private async Task ProcessAsync()
    {
        await foreach (var (message, context) in _channel.Reader.ReadAllAsync(_cts.Token))
        {
            Dispatch(message, context);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            await _processor.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        _cts.Dispose();
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