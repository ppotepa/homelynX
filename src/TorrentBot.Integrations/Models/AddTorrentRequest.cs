namespace TorrentBot.Integrations.Models;

public sealed record AddTorrentRequest(
    string UrlOrMagnet,
    string? SavePath = null,
    string? Category = null);