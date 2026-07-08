using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Engine.Pipeline.ResponseArtifacts;

namespace TorrentBot.Engine.Pipeline;

public sealed class ContractResponseConstructor : IResponseConstructor
{
    public IReadOnlyList<IExecutionArtifact> Construct(
        CapabilityContract? contract,
        ExecutionResult result,
        ConversationContext? conversation = null)
    {
        var enriched = EnrichResultData(contract, result, conversation);
        return ResponseArtifactBuilders.Build(contract?.ResponseSpec, enriched, conversation);
    }

    internal static ExecutionResult EnrichResultData(
        CapabilityContract? contract,
        ExecutionResult result,
        ConversationContext? conversation)
    {
        if (result.CapabilityResult?.Data is not Dictionary<string, object?> data)
        {
            return result;
        }

        var enriched = new Dictionary<string, object?>(data, StringComparer.Ordinal);
        var spec = contract?.ResponseSpec;
        if (spec is not null)
        {
            enriched["artifactKind"] = spec.ArtifactKind;
            if (!string.IsNullOrWhiteSpace(spec.FormatHint))
            {
                enriched["formatHint"] = spec.FormatHint;
            }
        }

        if (spec?.UseConversationState == true && conversation is not null && enriched.ContainsKey("results"))
        {
            enriched["conversationSessionId"] = conversation.SessionId;
        }

        return result with { CapabilityResult = result.CapabilityResult with { Data = enriched } };
    }
}