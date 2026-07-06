using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Presentation;

namespace TorrentBot.Presentation;

public sealed class SearchResultsPresenter : IArtifactPresenter
{
    public bool CanPresent(IExecutionArtifact artifact, RenderContext context) =>
        artifact is SearchResultsArtifact;

    public RenderedOutput Present(IExecutionArtifact artifact, RenderContext context)
    {
        var search = (SearchResultsArtifact)artifact;
        return context.Channel switch
        {
            RenderChannel.Cli when context.Format == RenderFormat.Table => PresentCliTable(search),
            RenderChannel.Cli => PresentCliPlain(search),
            _ => PresentTelegram(search)
        };
    }

    private static RenderedOutput PresentTelegram(SearchResultsArtifact search)
    {
        var lines = new List<string>
        {
            $"Wyniki: {search.Query} ({search.TotalCount}) — strona {search.Page + 1}/{search.TotalPages}",
            string.Empty
        };

        foreach (var item in search.Items)
        {
            lines.Add($"{item.Index}. {TrimName(item.Name)}");
            lines.Add($"   {FormatSize(item.SizeBytes)} | {FormatSeeders(item.Seeders)} seederow");
            lines.Add(string.Empty);
        }

        lines.Add("Pobierz: /select N");
        if (search.HasMore)
        {
            lines.Add("Wiecej: /more");
        }

        lines.Add("Anuluj: /cancel_search");

        var buttons = new List<RenderedButton>();
        foreach (var item in search.Items.Take(3))
        {
            buttons.Add(new RenderedButton($"Pobierz {item.Index}", $"select:{item.Index - 1}"));
        }

        if (search.HasMore)
        {
            buttons.Add(new RenderedButton("Nastepna strona", "more:1"));
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new RenderedOutput(string.Join('\n', lines), buttons);
    }

    private static RenderedOutput PresentCliPlain(SearchResultsArtifact search)
    {
        var lines = new List<string> { $"Search: {search.Query} ({search.TotalCount}) page {search.Page + 1}/{search.TotalPages}" };
        foreach (var item in search.Items)
        {
            lines.Add($"  [{item.Index}] {item.Name} | {FormatSize(item.SizeBytes)} | seeds={FormatSeeders(item.Seeders)}");
        }

        return new RenderedOutput(string.Join(Environment.NewLine, lines));
    }

    private static RenderedOutput PresentCliTable(SearchResultsArtifact search)
    {
        var lines = new List<string>
        {
            $"{"#",-3} {"Name",-50} {"Size",10} {"Seeds",6}",
            new string('-', 72)
        };

        foreach (var item in search.Items)
        {
            lines.Add($"{item.Index,-3} {TrimName(item.Name),-50} {FormatSize(item.SizeBytes),10} {FormatSeeders(item.Seeders),6}");
        }

        return new RenderedOutput(string.Join(Environment.NewLine, lines));
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:0.##} GB"
        : bytes >= 1_048_576 ? $"{bytes / 1_048_576.0:0.##} MB"
        : bytes >= 1024 ? $"{bytes / 1024.0:0.##} KB"
        : $"{bytes} B";

    private static string FormatSeeders(int? seeders) => seeders?.ToString() ?? "?";

    private static string TrimName(string name) =>
        name.Length <= 70 ? name : name[..67] + "...";
}