using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;

namespace TorrentBot.Adapters.Cli;

public sealed class CliInvocationAdapter
{
    public Invocation ToInvocation(string text, UserContext user, bool isDryRun = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(user);

        var trimmed = text.Trim();

        // Check if it's a slash command
        if (trimmed.StartsWith('/'))
        {
            return ParseSlashCommand(trimmed, user, isDryRun);
        }

        // Otherwise it's natural language
        return new Invocation
        {
            IsExplicit = false,
            Text = trimmed,
            IsDryRun = isDryRun,
            RequestContext = new RequestContext(
                Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),
                user.UserId,
                source: "cli"),
            User = user
        };
    }

    private static Invocation ParseSlashCommand(string text, UserContext user, bool isDryRun)
    {
        // Remove leading slash
        var command = text[1..];
        
        // Split into command and parameters
        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var commandName = parts[0];
        var parametersText = parts.Length > 1 ? parts[1] : null;

        // Parse parameters
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(parametersText))
        {
            // Simple parameter parsing: key=value or just value
            var paramParts = parametersText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in paramParts)
            {
                var eqIndex = part.IndexOf('=');
                if (eqIndex > 0)
                {
                    var key = part[..eqIndex];
                    var value = part[(eqIndex + 1)..];
                    parameters[key] = value;
                }
                else
                {
                    // If no key, use "query" as default (common for search commands)
                    parameters["query"] = part;
                }
            }
        }

        return new Invocation
        {
            IsExplicit = true,
            Command = $"/{commandName}",
            Parameters = parameters.Count > 0 ? parameters : null,
            IsDryRun = isDryRun,
            RequestContext = new RequestContext(
                Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),
                user.UserId,
                source: "cli"),
            User = user
        };
    }
}
