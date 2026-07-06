using TorrentBot.Contracts.Llm;

namespace TorrentBot.Llm;

internal static class PlanEnvelopeFactory
{
    public static PlanEnvelope Unsupported(string notes) =>
        new("unsupported", [], Confidence: 0, Notes: notes);
}