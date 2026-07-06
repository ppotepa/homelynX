namespace TorrentBot.Contracts.Jobs;

public enum JobKind
{
    Transient,
    LongLived,
    Recurring,
    Control
}