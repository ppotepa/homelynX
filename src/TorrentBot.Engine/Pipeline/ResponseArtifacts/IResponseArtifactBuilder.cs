using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;

namespace TorrentBot.Engine.Pipeline.ResponseArtifacts;

internal interface IResponseArtifactBuilder
{
    string ArtifactKind { get; }

    IReadOnlyList<IExecutionArtifact> Build(
        ResponseConstructionSpec? spec,
        ExecutionResult result,
        ConversationContext? conversation);
}