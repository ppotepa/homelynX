namespace TorrentBot.Contracts.Jobs;

public sealed record Job(
    string Id,
    string Type,
    JobKind Kind,
    object Payload,
    JobStatus Status,
    double Progress,
    object? Result,
    string? Error,
    string? ExternalId,
    string? ExternalSystem,
    bool SupportsCancellation,
    bool SupportsPause,
    TimeSpan? EstimatedTotalDuration,
    DateTimeOffset? ExpiresAt,
    Dictionary<string, string>? Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ParentJobId,
    string? OwnerUserId);