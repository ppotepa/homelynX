namespace TorrentBot.Adapters.Telegram.Verbosity;

public sealed class VerbosityStageMessage
{
    public required string Stage { get; init; }
    public string? Detail { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? TraceId { get; init; }
    public string? InvocationId { get; init; }
}