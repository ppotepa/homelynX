namespace TorrentBot.Contracts.Artifacts;

public sealed record DownloadStartedArtifact(
    string Name,
    string Provider,
    string? JobId,
    string? DownloadId) : IExecutionArtifact
{
    public string Kind => "download_started";
}