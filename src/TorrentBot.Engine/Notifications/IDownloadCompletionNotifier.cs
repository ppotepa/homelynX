using TorrentBot.Engine.Jobs;

namespace TorrentBot.Engine.Notifications;

public interface IDownloadCompletionNotifier
{
    void Notify(DownloadCompletedEvent completedEvent);
}

public sealed class RecordingDownloadCompletionNotifier : IDownloadCompletionNotifier
{
    private readonly List<DownloadCompletedEvent> _events = [];

    public IReadOnlyList<DownloadCompletedEvent> Events
    {
        get { lock (_events) { return _events.ToList(); } }
    }

    public void Notify(DownloadCompletedEvent completedEvent)
    {
        lock (_events)
        {
            _events.Add(completedEvent);
        }
    }
}