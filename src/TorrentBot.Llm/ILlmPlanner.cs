using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Llm;
using TorrentBot.Contracts.Pipeline;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Llm;

public sealed record LlmPlanningRequest(
    string Text,
    IReadOnlyList<CapabilityMetadata> Capabilities,
    IReadOnlyList<QuerySourceMeta> QuerySources,
    string? Scope = "media",
    ConversationContext? Conversation = null,
    int RequestNumber = 0,
    IProgressReporter? ProgressReporter = null,
    IReadOnlyList<CapabilityContract>? Contracts = null);

public interface ILlmPlanner
{
    Task<PlanEnvelope> PlanAsync(LlmPlanningRequest request, CancellationToken ct = default);
}