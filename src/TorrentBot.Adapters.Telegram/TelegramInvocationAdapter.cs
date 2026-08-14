using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Engine;

namespace TorrentBot.Adapters.Telegram;

public sealed class TelegramInvocationAdapter
{
    private readonly Func<string, string?>? _resolveCommand;

    public TelegramInvocationAdapter(Func<string, string?>? resolveCommand = null) =>
        _resolveCommand = resolveCommand;

    public Invocation ToInvocation(ITelegramUpdate update, UserContext user)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(user);

        var traceId = Guid.NewGuid().ToString("N");
        var invocationId = Guid.NewGuid().ToString("N");
        var requestContext = new RequestContext(
            traceId,
            invocationId,
            user.UserId,
            source: "telegram",
            chatId: update.ChatId.ToString(),
            messageId: update.MessageId?.ToString());

        if (update.IsCallback)
        {
            if (TryMapCallbackInvocation(update.CallbackData, out var callbackCapability, out var callbackParameters))
            {
                return new Invocation
                {
                    IsExplicit = true,
                    CapabilityName = callbackCapability,
                    Command = update.CallbackData,
                    Parameters = callbackParameters,
                    RequestContext = requestContext,
                    User = user
                };
            }

            return new Invocation
            {
                IsExplicit = true,
                Command = update.CallbackData,
                Parameters = ParseCallbackParameters(update.CallbackData),
                RequestContext = requestContext,
                User = user
            };
        }

        var text = update.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Invocation
            {
                IsExplicit = false,
                Text = string.Empty,
                RequestContext = requestContext,
                User = user
            };
        }

        if (text.StartsWith('/'))
        {
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var command = SlashCommandRouting.NormalizeCommand(parts[0]);
            var capabilityName = ResolveCapabilityName(command);
            var parameters = SlashCommandRouting.ParseParameters(command, parts.Length > 1 ? parts[1] : null);

            return new Invocation
            {
                IsExplicit = true,
                Command = command,
                CapabilityName = capabilityName,
                Parameters = parameters,
                RequestContext = requestContext,
                User = user
            };
        }

        return new Invocation
        {
            IsExplicit = false,
            Text = text,
            RequestContext = requestContext,
            User = user
        };
    }

    private string? ResolveCapabilityName(string command)
    {
        var overridden = SlashCommandRouting.ResolveCapabilityOverride(command);
        if (overridden is not null)
        {
            return overridden;
        }

        return _resolveCommand?.Invoke(command);
    }

    private static bool TryMapCallbackInvocation(
        string? callbackData,
        out string capabilityName,
        out IReadOnlyDictionary<string, object?> parameters)
    {
        capabilityName = string.Empty;
        parameters = new Dictionary<string, object?>();
        if (string.IsNullOrWhiteSpace(callbackData))
        {
            return false;
        }

        if (callbackData.StartsWith("select:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(callbackData["select:".Length..], out var selectIndex))
        {
            capabilityName = "torrent.select_result";
            parameters = new Dictionary<string, object?> { ["index"] = selectIndex };
            return true;
        }

        if (callbackData.StartsWith("more:", StringComparison.OrdinalIgnoreCase))
        {
            capabilityName = "torrent.more_results";
            return true;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, object?>? ParseCallbackParameters(string? callbackData)
    {
        if (string.IsNullOrWhiteSpace(callbackData))
        {
            return null;
        }

        if (!callbackData.Contains(':', StringComparison.Ordinal))
        {
            return new Dictionary<string, object?> { ["callback"] = callbackData };
        }

        var parts = callbackData.Split(':', 2);
        return new Dictionary<string, object?>
        {
            ["action"] = parts[0],
            ["token"] = parts[1]
        };
    }
}