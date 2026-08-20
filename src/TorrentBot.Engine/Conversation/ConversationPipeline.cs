using TorrentBot.Contracts.Bus.Events;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Conversation;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine.Bus;
using TorrentBot.Engine.Capabilities;
using TorrentBot.Engine.Context;
using TorrentBot.Engine.Pipeline;

namespace TorrentBot.Engine.Conversation;

public interface IConversationPipeline
{
    Task<PipelineResult?> ProcessUserResponseAsync(
        UserResponse response,
        ConversationContext context,
        Invocation baseInvocation,
        CancellationToken ct = default);

    void RegisterPendingFromResult(
        ConversationContext context,
        string capabilityName,
        CapabilityContract? contract,
        IReadOnlyDictionary<string, object?>? parameters,
        CapabilityResult result,
        IRequestContext? requestContext = null);

    Task<PipelineResult> ExecuteDirectCapabilityAsync(
        string capabilityName,
        IReadOnlyDictionary<string, object?> parameters,
        Invocation baseInvocation,
        CancellationToken ct = default);
}

public sealed class ConversationPipeline : IConversationPipeline
{
    private readonly IEngine _engine;
    private readonly IInvocationPipeline _invocationPipeline;
    private readonly CapabilityRegistry _capabilities;
    private readonly IInternalBus _bus;

    public ConversationPipeline(
        IEngine engine,
        IInvocationPipeline invocationPipeline,
        CapabilityRegistry capabilities,
        IInternalBus bus)
    {
        _engine = engine;
        _invocationPipeline = invocationPipeline;
        _capabilities = capabilities;
        _bus = bus;
    }

    public async Task<PipelineResult?> ProcessUserResponseAsync(
        UserResponse response,
        ConversationContext context,
        Invocation baseInvocation,
        CancellationToken ct = default)
    {
        var requestContext = baseInvocation.RequestContext
            ?? throw new InvalidOperationException("Invocation request context is required.");
        _bus.Publish(new UserResponseReceivedEvent(
            response.Token,
            response.UserId,
            response.ResponseType,
            response.RawValue), requestContext);

        var resolution = context.ResolvePendingAction(response.Token, response);
        if (!resolution.Resolved || resolution.Action is null)
        {
            return null;
        }

        if (string.Equals(response.ResponseType, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            _bus.Publish(new ConversationStateChangedEvent(
                context.SessionId,
                context.UserId,
                context.PendingActions.Count,
                "user_response_cancelled"), requestContext);

            return new PipelineResult(
                false,
                ArtifactAccumulator.FromMessage("Action cancelled."),
                "Action cancelled.");
        }

        var invocation = new Invocation
        {
            IsExplicit = true,
            CapabilityName = resolution.Action.CapabilityName,
            Parameters = resolution.Parameters,
            RequestContext = requestContext,
            User = baseInvocation.User,
            IsDryRun = baseInvocation.IsDryRun,
            ProgressReporter = baseInvocation.ProgressReporter
        };

        _bus.Publish(new ConversationStateChangedEvent(
            context.SessionId,
            context.UserId,
            context.PendingActions.Count,
            "user_response_resolved"), requestContext);

        return await _invocationPipeline.RunAsync(invocation, ct).ConfigureAwait(false);
    }

    public async Task<PipelineResult> ExecuteDirectCapabilityAsync(
        string capabilityName,
        IReadOnlyDictionary<string, object?> parameters,
        Invocation baseInvocation,
        CancellationToken ct = default)
    {
        var requestContext = baseInvocation.RequestContext
            ?? throw new InvalidOperationException("Invocation request context is required.");
        _bus.Publish(new UserResponseReceivedEvent(
            $"direct:{capabilityName}",
            baseInvocation.User.UserId,
            "direct",
            capabilityName), requestContext);

        var invocation = new Invocation
        {
            IsExplicit = true,
            CapabilityName = capabilityName,
            Parameters = parameters,
            RequestContext = requestContext,
            User = baseInvocation.User,
            IsDryRun = baseInvocation.IsDryRun,
            ProgressReporter = baseInvocation.ProgressReporter
        };

        var result = await _invocationPipeline.RunAsync(invocation, ct).ConfigureAwait(false);
        _bus.Publish(new ConversationStateChangedEvent(
            requestContext.ChatId ?? "default",
            baseInvocation.User.UserId,
            0,
            "direct_capability_executed"), requestContext);

        return result;
    }

    public void RegisterPendingFromResult(
        ConversationContext context,
        string capabilityName,
        CapabilityContract? contract,
        IReadOnlyDictionary<string, object?>? parameters,
        CapabilityResult result,
        IRequestContext? requestContext = null) =>
        ConversationPendingRegistrar.Register(
            context,
            capabilityName,
            contract,
            parameters,
            result,
            _capabilities,
            _bus,
            requestContext);
}
