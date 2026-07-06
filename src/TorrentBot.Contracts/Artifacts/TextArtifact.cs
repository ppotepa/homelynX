namespace TorrentBot.Contracts.Artifacts;

public sealed record TextArtifact(
    string Message,
    object? Data = null) : IExecutionArtifact
{
    public string Kind => "text";
}