using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Conversation;

namespace TorrentBot.Contracts.Context;

public sealed record ConversationMessage(
    string Role,
    string Content,
    DateTime Timestamp,
    int RequestNumber);

public sealed record ContextSnapshot(
    IReadOnlyDictionary<string, object?> State,
    DateTime CollectedAt);

public sealed class ConversationContext
{
    private readonly List<ConversationMessage> _history = [];
    private readonly Dictionary<string, ContextSnapshot> _snapshots = [];
    private readonly List<PendingUserAction> _pendingActions = [];
    private int _requestCounter;

    public string SessionId { get; }
    public string UserId { get; }
    public int RequestCount => _requestCounter;
    public IReadOnlyList<ConversationMessage> History => _history;
    public IReadOnlyDictionary<string, ContextSnapshot> Snapshots => _snapshots;
    public IReadOnlyList<PendingUserAction> PendingActions => _pendingActions;

    public ConversationContext(string sessionId, string userId)
    {
        SessionId = sessionId;
        UserId = userId;
    }

    public int NextRequestNumber() => ++_requestCounter;

    public void AddMessage(string role, string content, int requestNumber)
    {
        _history.Add(new ConversationMessage(role, content, DateTime.UtcNow, requestNumber));
        if (_history.Count > 20)
        {
            _history.RemoveAt(0);
        }
    }

    public void UpdateSnapshot(string source, ContextSnapshot snapshot)
    {
        _snapshots[source] = snapshot;
    }

    public ContextSnapshot? GetSnapshot(string source) =>
        _snapshots.TryGetValue(source, out var snapshot) ? snapshot : null;

    public void ClearSnapshots() => _snapshots.Clear();

    public void RemoveSnapshot(string source) => _snapshots.Remove(source);

    public void AddPendingAction(PendingUserAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _pendingActions.RemoveAll(a => string.Equals(a.Token, action.Token, StringComparison.Ordinal));
        _pendingActions.Add(action);
    }

    public PendingActionResolution ResolvePendingAction(string token, UserResponse response)
    {
        var action = _pendingActions.FirstOrDefault(a => string.Equals(a.Token, token, StringComparison.Ordinal));
        if (action is null)
        {
            return new PendingActionResolution(false, Error: "Pending action not found.");
        }

        if (!string.Equals(action.ExpectedResponse.Type, response.ResponseType, StringComparison.OrdinalIgnoreCase)
            && !MatchesResponseType(action.ExpectedResponse, response))
        {
            return new PendingActionResolution(false, Error: $"Expected response type '{action.ExpectedResponse.Type}'.");
        }

        _pendingActions.Remove(action);
        var parameters = BuildParameters(action, response);
        return new PendingActionResolution(true, action, parameters);
    }

    public IReadOnlyList<PendingUserAction> GetPendingActions() => _pendingActions.ToList();

    public void ClearPendingActions() => _pendingActions.Clear();

    private static bool MatchesResponseType(ExpectedResponseShape expected, UserResponse response) =>
        (expected.Type, response.ResponseType.ToLowerInvariant()) switch
        {
            ("yes_no", "confirm") => true,
            ("yes_no", "cancel") => true,
            ("yes_no", "text") => YesNoResponseParsing.TryParse(response.RawValue, out _),
            ("index", "select") => true,
            ("index", "text") => IndexSelectionParsing.TryParseDisplayIndex(response.RawValue, out _),
            ("token", "confirm") => true,
            _ => false
        };

    private static IReadOnlyDictionary<string, object?> BuildParameters(
        PendingUserAction action,
        UserResponse response)
    {
        var parameters = action.Parameters is not null
            ? new Dictionary<string, object?>(action.Parameters, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);

        if (response.ParsedParameters is not null)
        {
            foreach (var (key, value) in response.ParsedParameters)
            {
                parameters[key] = value;
            }
        }

        if (string.Equals(action.ExpectedResponse.Type, "index", StringComparison.OrdinalIgnoreCase)
            && IndexSelectionParsing.TryParseDisplayIndex(response.RawValue, out var displayIndex))
        {
            parameters["index"] = displayIndex;
        }
        else if (!string.IsNullOrWhiteSpace(response.RawValue)
            && !string.IsNullOrWhiteSpace(action.ExpectedResponse.ParameterName))
        {
            parameters[action.ExpectedResponse.ParameterName] = response.RawValue;
        }

        if (string.Equals(action.ExpectedResponse.Type, "yes_no", StringComparison.OrdinalIgnoreCase)
            && string.Equals(response.ResponseType, "text", StringComparison.OrdinalIgnoreCase)
            && YesNoResponseParsing.TryParse(response.RawValue, out var yesNoType))
        {
            parameters["confirmationDecision"] = yesNoType;
        }

        if (string.Equals(response.ResponseType, "confirm", StringComparison.OrdinalIgnoreCase))
        {
            parameters["confirmationToken"] = response.Token;
        }

        return parameters;
    }

}
