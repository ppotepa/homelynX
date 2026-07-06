using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Acl;

public static class AclMatcher
{
    private static readonly string[] Tiers = ["ADMIN", "ALL", "USER", "SAY", "PUBLIC"];

    public static bool SelectorMatches(string selector, CapabilityMetadata capability)
    {
        selector = (selector ?? string.Empty).Trim();
        if (selector.Length == 0)
        {
            return false;
        }

        var upper = selector.ToUpperInvariant();
        var permission = (capability.Permission ?? "ALL").ToUpperInvariant();

        if (Tiers.Contains(upper))
        {
            return upper switch
            {
                "ADMIN" => true,
                "ALL" or "USER" when permission is "ALL" or "PUBLIC" or "SAY" => true,
                "SAY" when permission is "SAY" or "PUBLIC" => true,
                _ => upper == permission
            };
        }

        var module = capability.Name.Contains('.') ? capability.Name.Split('.')[0] : capability.Name;
        var lowered = selector.ToLowerInvariant();
        if (lowered == capability.Name.ToLowerInvariant())
        {
            return true;
        }

        if (lowered == module.ToLowerInvariant())
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(capability.Command)
            && lowered == capability.Command.TrimStart('/').ToLowerInvariant())
        {
            return true;
        }

        return false;
    }

    public static bool Allows(string? userPermission, CapabilityMetadata capability)
    {
        var permission = (capability.Permission ?? "ALL").ToUpperInvariant();
        if (permission == "PUBLIC")
        {
            return true;
        }

        var tokens = (userPermission ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var grants = tokens.Where(t => !t.StartsWith('!')).ToArray();
        var denies = tokens.Where(t => t.StartsWith('!')).Select(t => t[1..]).ToArray();

        if (denies.Any(deny => SelectorMatches(deny, capability)))
        {
            return false;
        }

        return grants.Any(grant => SelectorMatches(grant, capability));
    }
}