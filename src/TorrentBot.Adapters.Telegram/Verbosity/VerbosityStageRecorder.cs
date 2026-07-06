using TorrentBot.Contracts.Bus;
using TorrentBot.Engine;

namespace TorrentBot.Adapters.Telegram.Verbosity;

public sealed class VerbosityStageRecorder : IDisposable
{
    private readonly List<VerbosityStageMessage> _stages = [];
    private readonly IDisposable? _subscription;

    public VerbosityStageRecorder(IEngine engine)
    {
        _subscription = engine.Subscribe<VerbosityStageMessage>(message =>
        {
            lock (_stages)
            {
                _stages.Add(message.Payload);
            }
        });
    }

    public IReadOnlyList<VerbosityStageMessage> Stages
    {
        get
        {
            lock (_stages)
            {
                return _stages.ToList();
            }
        }
    }

    public void Record(string stage, string? detail = null, string? traceId = null, string? invocationId = null)
    {
        lock (_stages)
        {
            _stages.Add(new VerbosityStageMessage
            {
                Stage = stage,
                Detail = detail,
                TraceId = traceId,
                InvocationId = invocationId
            });
        }
    }

    public void Dispose() => _subscription?.Dispose();
}