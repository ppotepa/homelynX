using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Engine;

namespace TorrentBot.Adapters.Cli;

public sealed class CliInvocationAdapter
{
    private readonly Func<string, string?>? _resolveCommand;

    public CliInvocationAdapter(Func<string, string?>? resolveCommand = null) =>
        _resolveCommand = resolveCommand;

    public Invocation ToInvocation(string text, UserContext user, bool isDryRun = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(user);

        var trimmed = text.Trim();

        if (trimmed.StartsWith('/'))
        {
            return ParseSlashCommand(trimmed, user, isDryRun);
        }

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

    private Invocation ParseSlashCommand(string text, UserContext user, bool isDryRun)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var command = SlashCommandRouting.NormalizeCommand(parts[0]);
        var capabilityName = SlashCommandRouting.ResolveCapabilityOverride(command)
            ?? _resolveCommand?.Invoke(command);
        var parameters = SlashCommandRouting.ParseParameters(command, parts.Length > 1 ? parts[1] : null);

        return new Invocation
        {
            IsExplicit = true,
            Command = command,
            CapabilityName = capabilityName,
            Parameters = parameters,
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