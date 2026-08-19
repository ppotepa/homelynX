namespace TorrentBot.Contracts.Bus.Events;

public sealed record AwaitUserResponseEvent(
    string Token,
    string CapabilityName,
    string ExpectedResponseType);

public sealed record UserResponseReceivedEvent(
    string Token,
    string UserId,
    string ResponseType,
    string? RawValue);

public sealed record ConversationStateChangedEvent(
    string SessionId,
    string UserId,
    int PendingActionCount,
    string ChangeKind);
