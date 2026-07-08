using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;

namespace TorrentBot.Contracts.Presentation;

public interface IResponseConstructor
{
    IReadOnlyList<IExecutionArtifact> Construct(
        CapabilityContract? contract,
        ExecutionResult result,
        ConversationContext? conversation = null);
}