namespace TorrentBot.Acl;

public sealed record AclRule(string Subject, string PermissionExpression);

public static class AclParser
{
    public static AclRule ParseLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            throw new FormatException("Empty or comment line");
        }

        var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new FormatException($"Invalid ACL line: {line}");
        }

        return new AclRule(parts[0].Trim(), parts[1].Trim());
    }

    public static IReadOnlyDictionary<string, string> ParseFile(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var rule = ParseLine(line);
            result[rule.Subject] = rule.PermissionExpression;
        }

        return result;
    }
}