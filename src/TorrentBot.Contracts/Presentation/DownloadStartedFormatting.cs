namespace TorrentBot.Contracts.Presentation;

public static class DownloadStartedFormatting
{
    public static string FormatMessage(string name, string provider, string? jobId, string? downloadId)
    {
        var text = $"Pobieranie rozpoczete: {name} ({provider})";
        if (!string.IsNullOrWhiteSpace(jobId))
        {
            text += $"\nJob: {jobId}";
        }

        if (!string.IsNullOrWhiteSpace(downloadId) && downloadId != jobId)
        {
            text += $"\nDownload: {downloadId}";
        }

        return text;
    }
}