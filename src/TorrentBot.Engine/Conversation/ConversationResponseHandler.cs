using TorrentBot.Contracts.Conversation;
using TorrentBot.Contracts.Context;
using TorrentBot.Engine.Context;

namespace TorrentBot.Engine.Conversation;

public sealed class ConversationResponseHandler
{
    private readonly ConversationContextStore _store;

    public ConversationResponseHandler(ConversationContextStore store)
    {
        _store = store;
    }

    public ConversationResponseResolution Resolve(string sessionId, string userId, string? callbackData, string? text = null)
    {
        var context = _store.GetOrCreate(sessionId, userId);

        if (!string.IsNullOrWhiteSpace(callbackData))
        {
            return ResolveCallback(context, userId, callbackData);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            return ResolveText(context, userId, text);
        }

        return ConversationResponseResolution.NotHandled;
    }

    private static ConversationResponseResolution ResolveCallback(ConversationContext context, string userId, string callbackData)
    {
        if (callbackData.StartsWith("pending:yes:", StringComparison.OrdinalIgnoreCase)
            || callbackData.StartsWith("pending:no:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = callbackData.Split(':', 4);
            if (parts.Length < 3)
            {
                return ConversationResponseResolution.Invalid("Malformed confirmation callback.");
            }

            var decision = parts[1].ToLowerInvariant();
            var token = parts[2];
            var responseType = decision switch
            {
                "yes" => "confirm",
                "no" or "cancel" => "cancel",
                _ => "confirm"
            };

            return ConversationResponseResolution.ForUserResponse(
                new UserResponse(token, userId, responseType, token),
                cancelled: responseType == "cancel");
        }

        if (callbackData.StartsWith("select:", StringComparison.OrdinalIgnoreCase))
        {
            var indexPart = callbackData["select:".Length..];
            if (!int.TryParse(indexPart, out var index))
            {
                return ConversationResponseResolution.Invalid("Invalid selection index.");
            }

            var pending = context.PendingActions.FirstOrDefault(a =>
                string.Equals(a.ExpectedResponse.Type, "index", StringComparison.OrdinalIgnoreCase));
            if (pending is null)
            {
                return ConversationResponseResolution.ForDirectCapability(
                    "torrent.select_result",
                    new Dictionary<string, object?> { ["index"] = index });
            }

            return ConversationResponseResolution.ForUserResponse(new UserResponse(
                pending.Token,
                userId,
                "select",
                indexPart,
                new Dictionary<string, object?> { ["index"] = index }));
        }

        return ConversationResponseResolution.NotHandled;
    }

    private static ConversationResponseResolution ResolveText(ConversationContext context, string userId, string text)
    {
        var indexPending = context.PendingActions.FirstOrDefault(a =>
            string.Equals(a.ExpectedResponse.Type, "index", StringComparison.OrdinalIgnoreCase));
        if (indexPending is not null)
        {
            if (!IndexSelectionParsing.TryParseDisplayIndex(text, out var index))
            {
                return ConversationResponseResolution.NotHandled;
            }

            return ConversationResponseResolution.ForUserResponse(new UserResponse(
                indexPending.Token,
                userId,
                "select",
                text,
                new Dictionary<string, object?> { ["index"] = index }));
        }

        var yesNoPending = context.PendingActions.FirstOrDefault(a =>
            string.Equals(a.ExpectedResponse.Type, "yes_no", StringComparison.OrdinalIgnoreCase));
        if (yesNoPending is not null && YesNoResponseParsing.TryParse(text, out var responseType))
        {
            return ConversationResponseResolution.ForUserResponse(
                new UserResponse(yesNoPending.Token, userId, responseType, text),
                cancelled: responseType == "cancel");
        }

        return ConversationResponseResolution.NotHandled;
    }
}

public sealed record ConversationResponseResolution(
    bool Handled,
    UserResponse? UserResponse = null,
    DirectCapabilityRequest? DirectCapability = null,
    bool Cancelled = false,
    string? Error = null)
{
    public static ConversationResponseResolution NotHandled => new(false);

    public static ConversationResponseResolution Invalid(string error) => new(true, Error: error);

    public static ConversationResponseResolution ForUserResponse(UserResponse response, bool cancelled = false) =>
        new(true, response, Cancelled: cancelled);

    public static ConversationResponseResolution ForDirectCapability(
        string capabilityName,
        IReadOnlyDictionary<string, object?> parameters) =>
        new(true, DirectCapability: new DirectCapabilityRequest(capabilityName, parameters));
}

public sealed record DirectCapabilityRequest(
    string CapabilityName,
    IReadOnlyDictionary<string, object?> Parameters);