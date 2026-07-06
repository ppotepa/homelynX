namespace TorrentBot.Engine.Confirmations;

public sealed class ConfirmationStore
{
    private readonly Dictionary<string, PendingConfirmation> _pending = new(StringComparer.Ordinal);

    public string Issue(string capabilityName, string userId, TimeSpan? ttl = null)
    {
        var token = Guid.NewGuid().ToString("N")[..12];
        _pending[token] = new PendingConfirmation(capabilityName, userId, DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(10)));
        return token;
    }

    public bool TryConsume(string token, string capabilityName, string userId)
    {
        if (!_pending.TryGetValue(token, out var pending))
        {
            return false;
        }

        _pending.Remove(token);
        if (pending.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return false;
        }

        return string.Equals(pending.CapabilityName, capabilityName, StringComparison.Ordinal)
               && string.Equals(pending.UserId, userId, StringComparison.Ordinal);
    }

    private sealed record PendingConfirmation(string CapabilityName, string UserId, DateTimeOffset ExpiresAt);
}