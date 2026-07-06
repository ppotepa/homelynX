using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Llm;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Llm;

public sealed record LlmPlanningRequest(
    string Text,
    IReadOnlyList<CapabilityMetadata> Capabilities,
    IReadOnlyList<QuerySourceMeta> QuerySources,
    string? Scope = "media");

public interface ILlmPlanner
{
    Task<PlanEnvelope> PlanAsync(LlmPlanningRequest request, CancellationToken ct = default);
}