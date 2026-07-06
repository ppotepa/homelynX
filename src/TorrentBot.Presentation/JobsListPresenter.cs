using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Presentation;

public sealed class JobsListPresenter : IArtifactPresenter
{
    public bool CanPresent(IExecutionArtifact artifact, RenderContext context) =>
        artifact is TextArtifact text
        && text.Data is Dictionary<string, object?> data
        && data.ContainsKey("jobs");

    public RenderedOutput Present(IExecutionArtifact artifact, RenderContext context)
    {
        var text = (TextArtifact)artifact;
        var data = (Dictionary<string, object?>)text.Data!;
        var jobs = ExtractRows(data["jobs"]);
        if (jobs.Count == 0)
        {
            return new RenderedOutput("Brak aktywnych zadan.");
        }

        var lines = new List<string> { $"Zadania ({jobs.Count}):" };
        foreach (var job in jobs)
        {
            var id = Get(job, "id") ?? "?";
            var type = Get(job, "type") ?? "?";
            var status = Get(job, "status") ?? "?";
            var progress = Get(job, "progress") ?? "0";
            lines.Add($"  {id} | {type} | {status} | {progress}");
        }

        lines.Add("Anuluj: /job_cancel <id>");
        return new RenderedOutput(string.Join('\n', lines));
    }

    private static List<Dictionary<string, object?>> ExtractRows(object? raw)
    {
        var rows = new List<Dictionary<string, object?>>();
        if (raw is not System.Collections.IEnumerable enumerable)
        {
            return rows;
        }

        foreach (var entry in enumerable)
        {
            if (entry is Dictionary<string, object?> dict)
            {
                rows.Add(dict);
            }
        }

        return rows;
    }

    private static string? Get(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? value?.ToString() : null;
}