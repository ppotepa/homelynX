using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;

namespace TorrentBot.Engine.Pipeline.ResponseArtifacts;

public static class ResponseArtifactBuilders
{
    private static readonly IReadOnlyDictionary<string, IResponseArtifactBuilder> Builders =
        new IResponseArtifactBuilder[]
        {
            new ListArtifactBuilder(),
            new SearchResultsArtifactBuilder(),
            new ConfirmationArtifactBuilder(),
            new DownloadStartedArtifactBuilder(),
            new TextArtifactBuilder()
        }.ToDictionary(b => b.ArtifactKind, StringComparer.Ordinal);

    private static readonly TextArtifactBuilder Fallback = new();

    public static IReadOnlyList<IExecutionArtifact> Build(
        ResponseConstructionSpec? spec,
        ExecutionResult result,
        ConversationContext? conversation = null)
    {
        if (!result.Success)
        {
            if (result.CapabilityResult?.Data is Dictionary<string, object?> failureData
                && failureData.TryGetValue("confirmationRequired", out var required)
                && required is true)
            {
                return Builders["confirmation"].Build(spec, result, conversation);
            }

            return
            [
                new ErrorArtifact(
                    ResolveErrorCode(result.Error),
                    result.Error ?? result.CapabilityResult?.Message ?? "Failed",
                    ResolveCapability(result))
            ];
        }

        var kind = spec?.ArtifactKind ?? InferKind(result);
        if (string.IsNullOrWhiteSpace(kind) || !Builders.TryGetValue(kind, out var builder))
        {
            builder = Fallback;
        }

        return builder.Build(spec, result, conversation);
    }

    private static string? InferKind(ExecutionResult result)
    {
        if (result.CapabilityResult?.Data is not Dictionary<string, object?> data)
        {
            return "text";
        }

        if (data.TryGetValue("artifactKind", out var kind) && !string.IsNullOrWhiteSpace(kind?.ToString()))
        {
            return kind.ToString();
        }

        if (data.TryGetValue("confirmationRequired", out var required) && required is true)
        {
            return "confirmation";
        }

        return "text";
    }

    private static string ResolveErrorCode(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "failed";
        }

        if (error.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            return "acl_denied";
        }

        if (error.Contains("not resolved", StringComparison.OrdinalIgnoreCase)
            || error.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "not_found";
        }

        if (error.Contains("Confirmation required", StringComparison.OrdinalIgnoreCase))
        {
            return "confirmation_required";
        }

        return "failed";
    }

    private static string? ResolveCapability(ExecutionResult result) =>
        result.CapabilityResult?.Data is Dictionary<string, object?> data
        && data.TryGetValue("capabilityName", out var name)
            ? name?.ToString()
            : null;
}