namespace TorrentBot.Contracts.Pipeline;

public interface IProgressReporter
{
    void Report(string stage, string? detail = null);
}
