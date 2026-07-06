using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Engine.Capabilities;

public sealed class CapabilityRegistry
{
    private readonly Dictionary<string, RegisteredCapability> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byCommand = new(StringComparer.OrdinalIgnoreCase);
    private bool _frozen;

    public void Register(CapabilityMetadata metadata, ICapabilityHandler handler)
    {
        if (_frozen)
        {
            throw new InvalidOperationException("Capability registry is frozen.");
        }

        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(handler);

        if (_byName.ContainsKey(metadata.Name))
        {
            throw new InvalidOperationException($"Capability '{metadata.Name}' is already registered.");
        }

        _byName[metadata.Name] = new RegisteredCapability(metadata, handler);

        if (!string.IsNullOrWhiteSpace(metadata.Command))
        {
            _byCommand[metadata.Command] = metadata.Name;
        }
    }

    public void Freeze() => _frozen = true;

    public CapabilityMetadata? GetMetadata(string name) =>
        _byName.TryGetValue(name, out var entry) ? entry.Metadata : null;

    public RegisteredCapability? Get(string name) =>
        _byName.TryGetValue(name, out var entry) ? entry : null;

    public string? ResolveCommand(string command) =>
        TryResolveCommand(command, out var name) ? name : null;

    public string? ResolveCommandFuzzy(string command, int maxDistance = 1)
    {
        if (TryResolveCommand(command, out var exact))
        {
            return exact;
        }

        var normalized = NormalizeCommand(command);
        if (normalized.Length == 0)
        {
            return null;
        }

        var stem = normalized.TrimStart('/');
        string? bestName = null;
        var bestDistance = maxDistance + 1;
        foreach (var (registeredCommand, capabilityName) in _byCommand)
        {
            var distance = LevenshteinDistance(stem, registeredCommand.TrimStart('/'));
            if (distance <= maxDistance && distance < bestDistance)
            {
                bestDistance = distance;
                bestName = capabilityName;
            }
        }

        return bestName;
    }

    public IReadOnlyList<string> GetRegisteredCommands() =>
        _byCommand.Keys.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<CapabilityMetadata> GetAllMetadata() =>
        _byName.Values.Select(v => v.Metadata).OrderBy(m => m.Name).ToList();

    private bool TryResolveCommand(string command, out string capabilityName)
    {
        capabilityName = string.Empty;
        var normalized = NormalizeCommand(command);
        return normalized.Length > 0 && _byCommand.TryGetValue(normalized, out capabilityName);
    }

    private static string NormalizeCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var normalized = command.Trim().ToLowerInvariant();
        var at = normalized.IndexOf('@');
        if (at > 0)
        {
            normalized = normalized[..at];
        }

        return normalized;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
        {
            return right.Length;
        }

        if (right.Length == 0)
        {
            return left.Length;
        }

        var costs = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
        {
            costs[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            var previous = costs[0];
            costs[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var current = costs[j];
                var substitution = left[i - 1] == right[j - 1] ? 0 : 1;
                costs[j] = Math.Min(
                    Math.Min(costs[j] + 1, costs[j - 1] + 1),
                    previous + substitution);
                previous = current;
            }
        }

        return costs[right.Length];
    }
}

public sealed record RegisteredCapability(CapabilityMetadata Metadata, ICapabilityHandler Handler);