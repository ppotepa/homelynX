namespace TorrentBot.Contracts.Artifacts;

public sealed record ErrorArtifact(
    string Code,
    string Message,
    string? CapabilityName = null) : IExecutionArtifact
{
    public string Kind => "error";
}