using TorrentBot.Engine.Confirmations;

namespace TorrentBot.Adapters.Telegram;

public sealed class ConfirmationCallbackHandler
{
    private readonly PendingInvocationStore _pendingInvocations;

    public ConfirmationCallbackHandler(
        ConfirmationStore? confirmationStore = null,
        PendingInvocationStore? pendingInvocations = null)
    {
        _ = confirmationStore;
        _pendingInvocations = pendingInvocations ?? new PendingInvocationStore();
    }

    public PendingInvocationStore PendingInvocations => _pendingInvocations;

    public void RegisterPending(string token, PendingInvocation invocation) =>
        _pendingInvocations.Register(token, invocation);

    public ConfirmationResolution Resolve(ITelegramUpdate update)
    {
        if (!update.IsCallback || string.IsNullOrWhiteSpace(update.CallbackData))
        {
            return ConfirmationResolution.NotHandled;
        }

        if (!update.CallbackData.StartsWith("confirm:", StringComparison.OrdinalIgnoreCase)
            && !update.CallbackData.StartsWith("cancel:", StringComparison.OrdinalIgnoreCase))
        {
            return ConfirmationResolution.NotHandled;
        }

        var parts = update.CallbackData.Split(':', 3);
        if (parts.Length < 2)
        {
            return ConfirmationResolution.Invalid("Malformed confirmation callback.");
        }

        var decision = parts[0].ToLowerInvariant();
        var token = parts[1];
        if (!_pendingInvocations.TryTake(token, update.UserId, out var pending))
        {
            return ConfirmationResolution.Invalid("Confirmation token was not found or expired.");
        }

        if (decision == "cancel")
        {
            return ConfirmationResolution.CreateRejected(pending.CapabilityName, token);
        }

        return ConfirmationResolution.CreateConfirmed(pending, token);
    }
}

public sealed record ConfirmationResolution(
    bool Handled,
    bool Confirmed,
    PendingInvocation? PendingInvocation = null,
    string? Action = null,
    string? Token = null,
    string? Error = null)
{
    public static ConfirmationResolution NotHandled => new(false, false);
    public static ConfirmationResolution Invalid(string error) => new(true, false, Error: error);
    public static ConfirmationResolution CreateConfirmed(PendingInvocation pending, string token) =>
        new(true, true, pending, pending.CapabilityName, token);
    public static ConfirmationResolution CreateRejected(string action, string token) =>
        new(true, false, Action: action, Token: token);
}