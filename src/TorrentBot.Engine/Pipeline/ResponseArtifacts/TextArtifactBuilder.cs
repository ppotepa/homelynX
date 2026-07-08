using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;

namespace TorrentBot.Engine.Pipeline.ResponseArtifacts;

internal sealed class TextArtifactBuilder : IResponseArtifactBuilder
{
    public string ArtifactKind => "text";

    public IReadOnlyList<IExecutionArtifact> Build(
        ResponseConstructionSpec? spec,
        ExecutionResult result,
        ConversationContext? conversation) =>
        [new TextArtifact(result.CapabilityResult?.Message ?? "OK", result.CapabilityResult?.Data)];
}