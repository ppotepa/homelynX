using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Engine.Context;
using TorrentBot.Engine.Conversation;

namespace TorrentBot.Engine.Pipeline;

public sealed class InvocationPipeline : IInvocationPipeline
{
    private readonly IEngine _engine;
    private readonly ConversationContextStore? _conversationStore;
    private readonly Func<IReadOnlyList<CapabilityContract>>? _contractsProvider;
    private readonly IResponseConstructor? _responseConstructor;
    private readonly Func<IConversationPipeline?>? _conversationPipelineProvider;

    public InvocationPipeline(
        IEngine engine,
        ConversationContextStore? conversationStore = null,
        Func<IReadOnlyList<CapabilityContract>>? contractsProvider = null,
        IResponseConstructor? responseConstructor = null,
        Func<IConversationPipeline?>? conversationPipelineProvider = null)
    {
        _engine = engine;
        _conversationStore = conversationStore;
        _contractsProvider = contractsProvider;
        _responseConstructor = responseConstructor;
        _conversationPipelineProvider = conversationPipelineProvider;
    }

    public async Task<PipelineResult> RunAsync(
        Invocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var sessionId = invocation.RequestContext?.ChatId
            ?? invocation.RequestContext?.TraceId
            ?? "default";
        var conversation = _conversationStore?.GetOrCreate(
            sessionId,
            invocation.User?.UserId ?? "unknown");

        var capabilityName = _engine is EngineHost host
            ? host.ResolveCapabilityName(invocation)
            : invocation.CapabilityName ?? invocation.Command;

        if (!string.IsNullOrWhiteSpace(capabilityName))
        {
            invocation.ProgressReporter?.Report("command:start", capabilityName);
        }

        var result = await _engine.SubmitAsync(invocation, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(capabilityName))
        {
            invocation.ProgressReporter?.Report(
                result.Success ? "command:done" : "command:error",
                result.Success ? capabilityName : $"{capabilityName}|{result.Error}");
        }

        var contract = string.IsNullOrWhiteSpace(capabilityName)
            ? null
            : _contractsProvider?.Invoke().FirstOrDefault(c =>
                string.Equals(c.Name, capabilityName, StringComparison.Ordinal));

        var enriched = ContractResponseConstructor.EnrichResultData(contract, result, conversation);
        var items = _responseConstructor?.Construct(contract, result, conversation);
        if (items is null || items.Count == 0)
        {
            items = ArtifactAccumulator.FromExecutionResult(
                enriched,
                spec: contract?.ResponseSpec).Items;
        }

        var artifacts = new ExecutionArtifacts(
            enriched.Success,
            items,
            enriched,
            enriched.Error);

        if (conversation is not null
            && !string.IsNullOrWhiteSpace(capabilityName)
            && enriched.CapabilityResult is not null)
        {
            _conversationPipelineProvider?.Invoke()?.RegisterPendingFromResult(
                conversation,
                capabilityName!,
                contract,
                invocation.Parameters,
                enriched.CapabilityResult,
                invocation.RequestContext);
        }

        return new PipelineResult(enriched.Success, artifacts, enriched.Error);
    }
}
