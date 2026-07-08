using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Contracts.Conversation;

public sealed record PendingUserAction(
    string Token,
    string CapabilityName,
    CapabilityContract Contract,
    ExpectedResponseShape ExpectedResponse,
    ContinuationRule? Continuation = null,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    DateTime CreatedAt = default)
{
    public DateTime CreatedAt { get; init; } = CreatedAt == default ? DateTime.UtcNow : CreatedAt;
}

public sealed record UserResponse(
    string Token,
    string UserId,
    string ResponseType,
    string? RawValue = null,
    IReadOnlyDictionary<string, object?>? ParsedParameters = null);

public sealed record PendingActionResolution(
    bool Resolved,
    PendingUserAction? Action = null,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    IReadOnlyList<PendingUserAction>? NewPendingActions = null,
    string? Error = null);