using TorrentBot.Contracts.Bus;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine;

namespace TorrentBot.Adapters.Telegram.Verbosity;

public sealed class VerbosityStageRecorder : IDisposable, IProgressReporter
{
    private readonly List<VerbosityStageMessage> _stages = [];
    private readonly IDisposable? _subscription;
    private readonly DateTimeOffset _createdAt = DateTimeOffset.UtcNow;

    public VerbosityStageRecorder(IEngine? engine = null)
    {
        if (engine is not null)
        {
            _subscription = engine.Subscribe<VerbosityStageMessage>(message =>
            {
                EmitStage(message.Payload);
            });
        }
    }

    public event Action<VerbosityStageMessage>? OnStage;

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

    public TimeSpan Elapsed => DateTimeOffset.UtcNow - _createdAt;

    public void Record(string stage, string? detail = null, string? traceId = null, string? invocationId = null)
    {
        EmitStage(new VerbosityStageMessage
        {
            Stage = stage,
            Detail = detail,
            TraceId = traceId,
            InvocationId = invocationId
        });
    }

    void IProgressReporter.Report(string stage, string? detail)
    {
        Record(stage, detail);
    }

    private void EmitStage(VerbosityStageMessage message)
    {
        VerbosityStageMessage stamped;
        lock (_stages)
        {
            _stages.Add(message);
            stamped = message;
        }
        OnStage?.Invoke(stamped);
    }

    public void Dispose() => _subscription?.Dispose();
}
