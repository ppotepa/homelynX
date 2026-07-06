namespace TorrentBot.Contracts.Artifacts;

public sealed record ConfirmationArtifact(
    string CapabilityName,
    string Token,
    string Message) : IExecutionArtifact
{
    public string Kind => "confirmation_required";
}