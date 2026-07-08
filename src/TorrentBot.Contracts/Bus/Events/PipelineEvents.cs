namespace TorrentBot.Contracts.Bus.Events;

public sealed record ToolCallEvent(string CapabilityName, IReadOnlyDictionary<string, object?>? Parameters);

public sealed record AwaitUserResponseEvent(
    string Token,
    string CapabilityName,
    string ExpectedResponseType);

public sealed record UserResponseReceivedEvent(
    string Token,
    string UserId,
    string ResponseType,
    string? RawValue);

public sealed record ResponseConstructedEvent(
    string CapabilityName,
    string ArtifactKind,
    bool Success);

public sealed record ConversationStateChangedEvent(
    string SessionId,
    string UserId,
    int PendingActionCount,
    string ChangeKind);