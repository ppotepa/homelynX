namespace TorrentBot.Adapters.Telegram.Verbosity;

public sealed class ProgressThrottler : IDisposable
{
    private readonly TimeSpan _minInterval;
    private DateTimeOffset _lastEdit = DateTimeOffset.MinValue;
    private string? _pendingText;
    private Func<string, CancellationToken, Task>? _editAction;
    private CancellationTokenSource? _cts;
    private Task? _flushTask;
    private readonly object _gate = new();

    public ProgressThrottler(TimeSpan? minInterval = null)
    {
        _minInterval = minInterval ?? TimeSpan.FromSeconds(1);
    }

    public void Configure(Func<string, CancellationToken, Task> editAction, CancellationToken ct)
    {
        lock (_gate)
        {
            _editAction = editAction;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }
    }

    public void Submit(string text, bool immediate = false)
    {
        lock (_gate)
        {
            _pendingText = text;

            if (immediate || _lastEdit == DateTimeOffset.MinValue)
            {
                FlushInternal();
                return;
            }

            var elapsed = DateTimeOffset.UtcNow - _lastEdit;
            if (elapsed >= _minInterval)
            {
                FlushInternal();
                return;
            }

            _flushTask ??= ScheduleFlushAsync(elapsed);
        }
    }

    public async Task FlushAsync()
    {
        Task? task;
        lock (_gate)
        {
            task = _flushTask;
        }
        if (task is not null)
        {
            await task.ConfigureAwait(false);
        }
        FlushInternal();
    }

    private Task ScheduleFlushAsync(TimeSpan delay)
    {
        var remaining = _minInterval - delay;
        if (remaining < TimeSpan.FromMilliseconds(50))
        {
            remaining = TimeSpan.FromMilliseconds(50);
        }

        return Task.Run(async () =>
        {
            try
            {
                await Task.Delay(remaining, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
                FlushInternal();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_gate)
                {
                    _flushTask = null;
                }
            }
        });
    }

    private void FlushInternal()
    {
        string? text;
        Func<string, CancellationToken, Task>? action;
        CancellationToken ct;
        lock (_gate)
        {
            text = _pendingText;
            _pendingText = null;
            action = _editAction;
            ct = _cts?.Token ?? CancellationToken.None;
            if (text is null || action is null) return;
            _lastEdit = DateTimeOffset.UtcNow;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await action(text, ct).ConfigureAwait(false);
            }
            catch
            {
            }
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
