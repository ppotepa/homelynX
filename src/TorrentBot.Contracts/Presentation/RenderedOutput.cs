namespace TorrentBot.Contracts.Presentation;

public sealed record RenderedOutput(
    string Text,
    IReadOnlyList<RenderedButton>? Buttons = null,
    string? Json = null,
    int ExitCode = 0);