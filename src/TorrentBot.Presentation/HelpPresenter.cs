using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Presentation;

public sealed class HelpPresenter : IArtifactPresenter
{
    public bool CanPresent(IExecutionArtifact artifact, RenderContext context) =>
        artifact is TextArtifact text
        && text.Data is Dictionary<string, object?> data
        && data.ContainsKey("capabilities");

    public RenderedOutput Present(IExecutionArtifact artifact, RenderContext context)
    {
        var text = (TextArtifact)artifact;
        var data = (Dictionary<string, object?>)text.Data!;
        var capabilities = ExtractCapabilities(data["capabilities"]);
        var lines = new List<string>
        {
            $"Dostepne komendy ({capabilities.Count}):",
            string.Empty
        };

        foreach (var group in capabilities
                     .Where(c => !string.IsNullOrWhiteSpace(c.Command))
                     .GroupBy(c => c.Module)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(group.Key.ToUpperInvariant());
            foreach (var capability in group.OrderBy(c => c.Command, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"  {capability.Command,-22} {Trim(capability.Description, 48)}");
            }

            lines.Add(string.Empty);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        lines.Add("Wiecej: /capabilities | NL: napisz co chcesz zrobic");

        return new RenderedOutput(string.Join('\n', lines));
    }

    private static List<CapabilityLine> ExtractCapabilities(object? raw)
    {
        var result = new List<CapabilityLine>();
        if (raw is not System.Collections.IEnumerable enumerable)
        {
            return result;
        }

        foreach (var entry in enumerable)
        {
            if (entry is not Dictionary<string, object?> dict)
            {
                continue;
            }

            var name = dict.TryGetValue("name", out var n) ? n?.ToString() ?? string.Empty : string.Empty;
            var command = dict.TryGetValue("command", out var c) ? c?.ToString() : null;
            var description = dict.TryGetValue("description", out var d) ? d?.ToString() ?? string.Empty : string.Empty;
            var module = name.Contains('.') ? name.Split('.')[0] : name;
            result.Add(new CapabilityLine(name, command, description, module));
        }

        return result;
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..(max - 3)] + "...";

    private sealed record CapabilityLine(string Name, string? Command, string Description, string Module);
}