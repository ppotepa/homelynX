namespace TorrentBot.Plugins.Tools;

/// <summary>Public bridge used by the ASP.NET host for short-link redirects.</summary>
public sealed class ShortLinkService(ToolsStore store)
{
    public Task<ShortLinkRecord?> ResolveAsync(string code, bool countVisit = true) => store.ResolveShortLink(code, countVisit);
}
