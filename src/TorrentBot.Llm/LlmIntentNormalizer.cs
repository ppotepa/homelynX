using TorrentBot.Llm.Polish;

namespace TorrentBot.Llm;

public sealed record LlmIntentContext(
    string OriginalText,
    string NormalizedText)
{
    public bool WasNormalized => !string.Equals(OriginalText, NormalizedText, StringComparison.Ordinal);
}

/// <summary>
/// Lightweight pre-processing before LLM planning. No capability or query inference — that is the planner's job.
/// </summary>
public static class LlmIntentNormalizer
{
    public static LlmIntentContext Analyze(string text)
    {
        var original = text ?? string.Empty;
        var normalizedText = PolishLexicon.NormalizeForLlm(original);
        return new LlmIntentContext(original, normalizedText);
    }
}