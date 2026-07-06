namespace TorrentBot.Contracts.Presentation;

public sealed record RenderContext(
    RenderChannel Channel,
    RenderFormat Format = RenderFormat.Plain);