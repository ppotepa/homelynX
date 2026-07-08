using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Engine.Pipeline.ResponseArtifacts;

internal sealed class ListArtifactBuilder : IResponseArtifactBuilder
{
    public string ArtifactKind => "list";

    public IReadOnlyList<IExecutionArtifact> Build(
        ResponseConstructionSpec? spec,
        ExecutionResult result,
        ConversationContext? conversation)
    {
        if (result.CapabilityResult?.Data is not Dictionary<string, object?> data)
        {
            return [new TextArtifact(result.CapabilityResult?.Message ?? "OK", result.CapabilityResult?.Data)];
        }

        if (string.IsNullOrWhiteSpace(spec?.ItemsKey)
            || !data.TryGetValue(spec.ItemsKey, out var raw))
        {
            return [new TextArtifact(result.CapabilityResult?.Message ?? "No list items.", data)];
        }

        var formatHint = spec.FormatHint ?? (data.TryGetValue("formatHint", out var hint) ? hint?.ToString() : null);
        var enumerable = raw is System.Collections.IEnumerable e ? e.Cast<object?>() : [];
        var message = ResponseFormatting.FormatListMessage(formatHint, enumerable);
        return [new TextArtifact(message, data)];
    }
}