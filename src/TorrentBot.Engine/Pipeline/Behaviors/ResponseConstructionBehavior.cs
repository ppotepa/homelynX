using TorrentBot.Contracts.Bus.Events;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Engine.Bus;

namespace TorrentBot.Engine.Pipeline.Behaviors;

public sealed class ResponseConstructionBehavior : IPipelineBehavior
{
    private readonly IResponseConstructor _constructor;
    private readonly IInternalBus? _bus;

    public ResponseConstructionBehavior(IResponseConstructor constructor, IInternalBus? bus = null)
    {
        _constructor = constructor;
        _bus = bus;
    }

    public string Name => "response_construction";

    public Task<PipelineBehaviorContext> BeforePlanAsync(PipelineBehaviorContext context, CancellationToken ct = default) =>
        Task.FromResult(context);

    public Task<(PipelineBehaviorContext Context, PipelineResult? UpdatedResult)> AfterExecutionAsync(
        PipelineBehaviorContext context,
        PipelineResult result,
        CancellationToken ct = default)
    {
        if (result.Artifacts.RawResult is null)
        {
            return Task.FromResult((context, (PipelineResult?)null));
        }

        var capabilityName = context.Invocation.CapabilityName
            ?? result.Plan?.Steps.LastOrDefault()?.CapabilityName;
        var contract = context.Contracts.FirstOrDefault(c =>
            string.Equals(c.Name, capabilityName, StringComparison.Ordinal));

        var items = _constructor.Construct(contract, result.Artifacts.RawResult, context.Conversation);
        var enriched = ContractResponseConstructor.EnrichResultData(contract, result.Artifacts.RawResult, context.Conversation);
        var updated = new PipelineResult(
            enriched.Success,
            new ExecutionArtifacts(enriched.Success, items, enriched, enriched.Error),
            result.Plan,
            enriched.Error);

        if (_bus is not null && context.Invocation.RequestContext is not null)
        {
            var kind = contract?.ResponseSpec?.ArtifactKind ?? "text";
            _bus.Publish(
                new ResponseConstructedEvent(capabilityName ?? "unknown", kind, enriched.Success),
                context.Invocation.RequestContext);
        }

        return Task.FromResult<(PipelineBehaviorContext Context, PipelineResult? UpdatedResult)>(
            (context.WithState("constructed_artifacts", items.Count), updated));
    }
}