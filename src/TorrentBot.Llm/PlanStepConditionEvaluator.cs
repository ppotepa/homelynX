namespace TorrentBot.Llm;

public static class PlanStepConditionEvaluator
{
    public static bool ShouldExecute(string? condition, IReadOnlyDictionary<string, object?> saved)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        if (condition.Equals("always", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (condition.Equals("never", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (condition.StartsWith("saved:", StringComparison.OrdinalIgnoreCase))
        {
            var key = condition["saved:".Length..];
            return saved.TryGetValue(key, out var value) && value is not null;
        }

        if (condition.StartsWith("not_saved:", StringComparison.OrdinalIgnoreCase))
        {
            var key = condition["not_saved:".Length..];
            return !saved.ContainsKey(key) || saved[key] is null;
        }

        return true;
    }
}