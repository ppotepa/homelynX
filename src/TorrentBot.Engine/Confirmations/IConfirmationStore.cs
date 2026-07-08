namespace TorrentBot.Engine.Confirmations;

public interface IConfirmationStore
{
    string Issue(string capabilityName, string userId, TimeSpan? ttl = null);
    bool TryConsume(string token, string capabilityName, string userId);
}
