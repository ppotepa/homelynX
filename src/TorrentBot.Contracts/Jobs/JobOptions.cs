namespace TorrentBot.Contracts.Jobs;

public sealed record JobOptions(
    TimeSpan? Ttl = null,
    bool SupportsPause = false,
    bool SupportsCancellation = true,
    JobKind Kind = JobKind.Transient);