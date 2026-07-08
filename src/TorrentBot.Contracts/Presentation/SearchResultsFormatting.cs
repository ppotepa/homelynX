using TorrentBot.Contracts.Artifacts;

namespace TorrentBot.Contracts.Presentation;

public static class SearchResultsFormatting
{
    public static string FormatPlain(SearchResultsArtifact search) =>
        $"Search: {search.Query} ({search.TotalCount}) page {search.Page + 1}/{search.TotalPages}\n"
        + string.Join('\n', search.Items.Select(item =>
            $"  [{item.Index}] {item.Name} | {FormatSize(item.SizeBytes)} | seeds={FormatSeeders(item.Seeders)}"));

    public static string FormatTelegram(SearchResultsArtifact search)
    {
        var lines = new List<string>
        {
            $"Wyniki: {search.Query} ({search.TotalCount}) — strona {search.Page + 1}/{search.TotalPages}",
            string.Empty
        };

        if (search.TotalCount == 0)
        {
            lines.Add("Brak wynikow dla tego zapytania.");
            lines.Add("Sprobuj innej frazy lub /download_search <query>");
        }
        else
        {
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
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines);
    }

    public static string FormatTable(SearchResultsArtifact search)
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

        return string.Join(Environment.NewLine, lines);
    }

    public static RenderedOutput Render(SearchResultsArtifact search, RenderContext context)
    {
        var text = context.Channel switch
        {
            RenderChannel.Cli when context.Format == RenderFormat.Table => FormatTable(search),
            RenderChannel.Cli => FormatPlain(search),
            _ => FormatTelegram(search)
        };

        if (context.Channel != RenderChannel.Telegram)
        {
            return new RenderedOutput(text);
        }

        var buttons = new List<RenderedButton>();
        foreach (var item in search.Items.Take(3))
        {
            buttons.Add(new RenderedButton($"Pobierz {item.Index}", $"select:{item.Index}"));
        }

        if (search.HasMore)
        {
            buttons.Add(new RenderedButton("Nastepna strona", "more:1"));
        }

        return new RenderedOutput(text, buttons);
    }

    public static string FormatSize(long bytes) =>
        bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:0.##} GB"
        : bytes >= 1_048_576 ? $"{bytes / 1_048_576.0:0.##} MB"
        : bytes >= 1024 ? $"{bytes / 1024.0:0.##} KB"
        : $"{bytes} B";

    public static string FormatSeeders(int? seeders) => seeders?.ToString() ?? "?";

    public static string TrimName(string name) =>
        name.Length <= 70 ? name : name[..67] + "...";
}