using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Engine.Pipeline.ResponseArtifacts;

internal sealed class SearchResultsArtifactBuilder : IResponseArtifactBuilder
{
    public string ArtifactKind => "search_results";

    public IReadOnlyList<IExecutionArtifact> Build(
        ResponseConstructionSpec? spec,
        ExecutionResult result,
        ConversationContext? conversation)
    {
        if (spec is null
            || result.CapabilityResult?.Data is not Dictionary<string, object?> data
            || !SearchResultsArtifactParser.TryParse(data, out var search, spec))
        {
            return [new TextArtifact(result.CapabilityResult?.Message ?? "No search results.", result.CapabilityResult?.Data)];
        }

        if (spec?.UseConversationState == true && conversation is not null)
        {
            data["conversationSessionId"] = conversation.SessionId;
        }

        return [search];
    }
}