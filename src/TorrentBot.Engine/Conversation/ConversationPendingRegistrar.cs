using TorrentBot.Contracts.Bus.Events;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Conversation;
using TorrentBot.Engine.Bus;
using TorrentBot.Engine.Capabilities;

namespace TorrentBot.Engine.Conversation;

internal static class ConversationPendingRegistrar
{
    public static void Register(
        ConversationContext context,
        string capabilityName,
        CapabilityContract? contract,
        IReadOnlyDictionary<string, object?>? parameters,
        CapabilityResult result,
        CapabilityRegistry capabilities,
        IInternalBus bus,
        IRequestContext? requestContext = null)
    {
        if (result.Data is not Dictionary<string, object?> data)
        {
            return;
        }

        if (data.TryGetValue("confirmationRequired", out var required)
            && required is true
            && data.TryGetValue("confirmationToken", out var tokenObj))
        {
            RegisterConfirmation(context, capabilityName, contract, parameters, capabilities, bus, requestContext, tokenObj);
            return;
        }

        if (!result.Success)
        {
            return;
        }

        RegisterContinuations(context, capabilityName, contract, parameters, result, capabilities, bus, requestContext);
    }

    private static void RegisterConfirmation(
        ConversationContext context,
        string capabilityName,
        CapabilityContract? contract,
        IReadOnlyDictionary<string, object?>? parameters,
        CapabilityRegistry capabilities,
        IInternalBus bus,
        IRequestContext? requestContext,
        object? tokenObj)
    {
        var effectiveContract = contract ?? capabilities.GetContract(capabilityName);
        if (effectiveContract is null)
        {
            return;
        }

        var token = tokenObj?.ToString() ?? Guid.NewGuid().ToString("N");
        context.AddPendingAction(new PendingUserAction(
            token,
            capabilityName,
            effectiveContract,
            effectiveContract.UserInteractions?.ExpectedResponseTypes?.Contains("confirm") == true
                ? new ExpectedResponseShape("yes_no")
                : new ExpectedResponseShape("token"),
            Continuation: null,
            parameters));

        PublishPendingAdded(bus, requestContext, context, token, capabilityName, "yes_no");
    }

    private static void RegisterContinuations(
        ConversationContext context,
        string capabilityName,
        CapabilityContract? contract,
        IReadOnlyDictionary<string, object?>? parameters,
        CapabilityResult result,
        CapabilityRegistry capabilities,
        IInternalBus bus,
        IRequestContext? requestContext)
    {
        var effectiveContract = contract ?? capabilities.GetContract(capabilityName);
        if (effectiveContract?.Continuations is not { Count: > 0 })
        {
            return;
        }

        foreach (var rule in effectiveContract.Continuations)
        {
            if (!ShouldTrigger(rule, result))
            {
                continue;
            }

            var token = Guid.NewGuid().ToString("N");
            var pending = new PendingUserAction(
                token,
                rule.NextCapability ?? capabilityName,
                effectiveContract,
                rule.ExpectedResponse ?? new ExpectedResponseShape("index", "index"),
                Continuation: null,
                parameters);
            context.AddPendingAction(pending);
            PublishPendingAdded(bus, requestContext, context, token, pending.CapabilityName, pending.ExpectedResponse.Type);
        }
    }

    private static void PublishPendingAdded(
        IInternalBus bus,
        IRequestContext? requestContext,
        ConversationContext context,
        string token,
        string capabilityName,
        string responseType)
    {
        if (requestContext is null)
        {
            return;
        }

        bus.Publish(new AwaitUserResponseEvent(token, capabilityName, responseType), requestContext);
        bus.Publish(
            new ConversationStateChangedEvent(context.SessionId, context.UserId, context.PendingActions.Count, "pending_added"),
            requestContext);
    }

    private static bool ShouldTrigger(ContinuationRule rule, CapabilityResult result)
    {
        if (string.Equals(rule.Trigger, "always", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!result.Success)
        {
            return false;
        }

        if (string.Equals(rule.Trigger, "on_success", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(rule.Trigger, "when_has_results", StringComparison.OrdinalIgnoreCase)
            && result.Data is Dictionary<string, object?> data
            && data.TryGetValue("count", out var count)
            && int.TryParse(count?.ToString(), out var c)
            && c > 0)
        {
            return true;
        }

        return string.Equals(rule.Trigger, "when_confirmation_required", StringComparison.OrdinalIgnoreCase)
            && result.Data is Dictionary<string, object?> d
            && d.TryGetValue("confirmationRequired", out var req)
            && req is true;
    }
}