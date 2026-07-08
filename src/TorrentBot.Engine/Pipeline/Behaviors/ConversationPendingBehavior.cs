using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine.Conversation;

namespace TorrentBot.Engine.Pipeline.Behaviors;

public sealed class ConversationPendingBehavior : IPipelineBehavior
{
    private readonly Func<IConversationPipeline> _conversationPipeline;

    public ConversationPendingBehavior(Func<IConversationPipeline> conversationPipeline)
    {
        _conversationPipeline = conversationPipeline;
    }

    public string Name => "conversation_pending";

    public Task<PipelineBehaviorContext> BeforePlanAsync(PipelineBehaviorContext context, CancellationToken ct = default) =>
        Task.FromResult(context);

    public Task<(PipelineBehaviorContext Context, PipelineResult? UpdatedResult)> AfterExecutionAsync(
        PipelineBehaviorContext context,
        PipelineResult result,
        CancellationToken ct = default)
    {
        if (context.Conversation is null || result.Artifacts.RawResult?.CapabilityResult is null)
        {
            return Task.FromResult((context, (PipelineResult?)null));
        }

        var capabilityName = context.Invocation.CapabilityName
            ?? result.Plan?.Steps.LastOrDefault()?.CapabilityName;
        if (string.IsNullOrWhiteSpace(capabilityName))
        {
            return Task.FromResult((context, (PipelineResult?)null));
        }

        var contract = context.Contracts.FirstOrDefault(c => c.Name == capabilityName);
        _conversationPipeline().RegisterPendingFromResult(
            context.Conversation,
            capabilityName,
            contract,
            context.Invocation.Parameters,
            result.Artifacts.RawResult.CapabilityResult,
            context.Invocation.RequestContext);

        return Task.FromResult((context, (PipelineResult?)null));
    }
}