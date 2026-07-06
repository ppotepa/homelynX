using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;

namespace TorrentBot.Adapters.Telegram;

public sealed class TelegramInvocationAdapter
{
    private readonly Func<string, string?>? _resolveCommand;

    private static readonly Dictionary<string, string> CommandCapabilityOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["/download_search"] = "torrent.search",
            ["/list"] = "system.help",
            ["/commands"] = "system.help"
        };

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
            var command = NormalizeSlashCommand(parts[0]);
            var capabilityName = ResolveCapabilityName(command);
            var parameters = ParseSlashParameters(command, parts.Length > 1 ? parts[1] : null);

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
        if (CommandCapabilityOverrides.TryGetValue(command, out var overridden))
        {
            return overridden;
        }

        return _resolveCommand?.Invoke(command);
    }

    private static string NormalizeSlashCommand(string raw)
    {
        var command = raw.Trim().ToLowerInvariant();
        var at = command.IndexOf('@');
        if (at > 0)
        {
            command = command[..at];
        }

        return command;
    }

    private static IReadOnlyDictionary<string, object?>? ParseSlashParameters(string command, string? remainder)
    {
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return null;
        }

        return command switch
        {
            "/search" or "/torrent_search" or "/download_search" => new Dictionary<string, object?> { ["query"] = remainder },
            "/select" => new Dictionary<string, object?> { ["index"] = int.TryParse(remainder, out var index) ? index : remainder },
            "/download_candidate" => new Dictionary<string, object?> { ["title"] = remainder, ["query"] = remainder },
            "/download" => ParseKeyValuePairs(remainder),
            "/pause" or "/resume" or "/cancel" => ParseControlParameters(remainder),
            "/torrent_pause" or "/torrent_resume" or "/torrent_delete" => new Dictionary<string, object?> { ["hash"] = remainder },
            "/job_cancel" => new Dictionary<string, object?> { ["jobId"] = remainder, ["id"] = remainder },
            "/find_large_files" => int.TryParse(remainder, out var minMb)
                ? new Dictionary<string, object?> { ["min_mb"] = minMb }
                : new Dictionary<string, object?> { ["text"] = remainder },
            _ => new Dictionary<string, object?> { ["text"] = remainder }
        };
    }

    private static Dictionary<string, object?> ParseControlParameters(string remainder)
    {
        if (remainder.StartsWith("job:", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object?> { ["jobId"] = remainder[4..] };
        }

        return new Dictionary<string, object?> { ["id"] = remainder, ["hash"] = remainder };
    }

    private static Dictionary<string, object?> ParseKeyValuePairs(string remainder)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var token in remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = token.IndexOf('=');
            if (idx > 0)
            {
                result[token[..idx]] = token[(idx + 1)..];
            }
            else if (!result.ContainsKey("url") && Uri.TryCreate(token, UriKind.Absolute, out _))
            {
                result["url"] = token;
                result["provider"] = "url";
            }
            else if (!result.ContainsKey("query"))
            {
                result["query"] = token;
            }
        }

        if (!result.ContainsKey("provider"))
        {
            result["provider"] = result.ContainsKey("url") ? "url" : "torrent";
        }

        return result;
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