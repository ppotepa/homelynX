using System.Text.Json;
using System.Text.Json.Serialization;

namespace TorrentBot.Engine.Tests.Support;

public sealed class LlmScenarioFile
{
    public List<LlmScenarioDefinition> Scenarios { get; init; } = [];
}

public sealed class LlmScenarioDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Input { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public LlmScenarioExpectation Expect { get; init; } = new();
}

public sealed class LlmScenarioExpectation
{
    public string? FirstCapability { get; init; }
    public List<string> AllowedCapabilities { get; init; } = [];
    public List<string> QueryContains { get; init; } = [];
    public List<string> QueryNotContains { get; init; } = [];
    public int MinSteps { get; init; } = 1;
    public int MaxSteps { get; init; } = 3;
    public bool AllowEmptyPlan { get; init; }
}

public static class LlmScenarioCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<LlmScenarioDefinition> LoadAll()
    {
        var path = ResolveScenarioPath();
        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<LlmScenarioFile>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse LLM scenarios from {path}");
        return file.Scenarios;
    }

    private static string ResolveScenarioPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Llm", "llm-scenarios.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Llm", "llm-scenarios.json")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "src", "TorrentBot.Engine.Tests", "Llm", "llm-scenarios.json"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("llm-scenarios.json not found", string.Join(", ", candidates));
    }
}