namespace TorrentBot.Contracts.Context;

public sealed record UserContext(string UserId, string[] Grants, string EffectiveProfile);