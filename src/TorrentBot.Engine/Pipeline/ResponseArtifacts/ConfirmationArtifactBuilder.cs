using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;

namespace TorrentBot.Engine.Pipeline.ResponseArtifacts;

internal sealed class ConfirmationArtifactBuilder : IResponseArtifactBuilder
{
    public string ArtifactKind => "confirmation";

    public IReadOnlyList<IExecutionArtifact> Build(
        ResponseConstructionSpec? spec,
        ExecutionResult result,
        ConversationContext? conversation)
    {
        if (result.CapabilityResult?.Data is not Dictionary<string, object?> data
            || !data.TryGetValue("confirmationRequired", out var required)
            || required is not true
            || !data.TryGetValue("confirmationToken", out var token))
        {
            return [new TextArtifact(
                result.CapabilityResult?.Message ?? result.Error ?? "Confirmation required.",
                result.CapabilityResult?.Data)];
        }

        var capability = data.TryGetValue("capabilityName", out var name) ? name?.ToString() ?? "unknown" : "unknown";
        var message = result.CapabilityResult?.Message
            ?? spec?.FormatHint
            ?? result.Error
            ?? "Confirmation required.";

        return
        [
            new ConfirmationArtifact(
                capability,
                token?.ToString() ?? string.Empty,
                message)
        ];
    }
}