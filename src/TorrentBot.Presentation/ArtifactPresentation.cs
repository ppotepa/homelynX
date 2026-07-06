using System.Text.Json;
using TorrentBot.Contracts.Artifacts;
using TorrentBot.Contracts.Presentation;
using TorrentBot.Contracts.Pipeline;

namespace TorrentBot.Presentation;

public sealed class ArtifactPresentation
{
    private readonly IReadOnlyList<IArtifactPresenter> _presenters;

    public ArtifactPresentation(IEnumerable<IArtifactPresenter> presenters) =>
        _presenters = presenters.ToList();

    public RenderedOutput Render(ExecutionArtifacts artifacts, RenderContext context)
    {
        if (context.Format == RenderFormat.Json)
        {
            return new RenderedOutput(
                Text: artifacts.RawResult?.CapabilityResult?.Message ?? artifacts.Error ?? string.Empty,
                Json: JsonSerializer.Serialize(artifacts, JsonOptions()),
                ExitCode: artifacts.Success ? 0 : 1);
        }

        if (artifacts.Items.Count == 0)
        {
            return new RenderedOutput(
                artifacts.Error ?? artifacts.RawResult?.CapabilityResult?.Message ?? "Done",
                ExitCode: artifacts.Success ? 0 : 1);
        }

        var parts = new List<string>();
        RenderedOutput? last = null;
        foreach (var item in artifacts.Items)
        {
            var presenter = _presenters.FirstOrDefault(p => p.CanPresent(item, context));
            if (presenter is null)
            {
                if (item is TextArtifact text)
                {
                    parts.Add(text.Message);
                }

                continue;
            }

            last = presenter.Present(item, context);
            parts.Add(last.Text);
        }

        var combined = string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return new RenderedOutput(
            combined,
            last?.Buttons,
            ExitCode: artifacts.Success ? 0 : 1);
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };
}