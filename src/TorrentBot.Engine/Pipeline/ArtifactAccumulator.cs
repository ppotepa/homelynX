using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Engine.Pipeline.ResponseArtifacts;

namespace TorrentBot.Engine.Pipeline;

public static class ArtifactAccumulator
{
    public static ExecutionArtifacts FromMessage(string message) =>
        new(false, [new TextArtifact(message)], null, message);

    public static ExecutionArtifacts FromExecutionResult(
        ExecutionResult result,
        string? expectedArtifactKind = null,
        ResponseConstructionSpec? spec = null)
    {
        var effectiveSpec = spec ?? (expectedArtifactKind is not null
            ? new ResponseConstructionSpec(expectedArtifactKind)
            : null);

        if (!result.Success
            && result.CapabilityResult?.Data is Dictionary<string, object?> failureData
            && failureData.TryGetValue("confirmationRequired", out var required)
            && required is true)
        {
            var items = ResponseArtifactBuilders.Build(
                effectiveSpec ?? new ResponseConstructionSpec("confirmation"),
                result);
            return new ExecutionArtifacts(false, items, result, result.Error);
        }

        if (!result.Success)
        {
            var code = ResolveErrorCode(result.Error);
            return new ExecutionArtifacts(
                false,
                [new ErrorArtifact(code, result.Error ?? "Failed", ResolveCapability(result))],
                result,
                result.Error);
        }

        var artifacts = ResponseArtifactBuilders.Build(effectiveSpec, result);
        if (artifacts.Count == 0)
        {
            artifacts = [new TextArtifact(result.CapabilityResult?.Message ?? "OK", result.CapabilityResult?.Data)];
        }

        return new ExecutionArtifacts(true, artifacts, result);
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