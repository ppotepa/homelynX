using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;

namespace TorrentBot.Adapters.Telegram;

public sealed record PendingInvocation(
    string CapabilityName,
    IReadOnlyDictionary<string, object?>? Parameters,
    IRequestContext RequestContext,
    UserContext User,
    bool IsDryRun);

public sealed class PendingInvocationStore
{
    private readonly Dictionary<string, PendingInvocation> _pending = new(StringComparer.Ordinal);

    public void Register(string token, PendingInvocation invocation) =>
        _pending[token] = invocation;

    public bool TryTake(string token, string userId, out PendingInvocation invocation)
    {
        invocation = default!;
        if (!_pending.TryGetValue(token, out var pending))
        {
            return false;
        }

        if (!string.Equals(pending.User.UserId, userId, StringComparison.Ordinal))
        {
            return false;
        }

        _pending.Remove(token);
        invocation = pending;
        return true;
    }
}