namespace TorrentBot.Contracts.Jobs;

public enum JobStatus
{
    Queued,
    Running,
    Paused,
    Waiting,
    Succeeded,
    Failed,
    Cancelled
}