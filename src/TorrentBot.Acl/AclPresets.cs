namespace TorrentBot.Acl;

public static class AclPresets
{
    public static readonly IReadOnlyDictionary<string, string> BuiltIn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["USER"] = "USER|ALL",
        ["ADMIN"] = "ADMIN"
    };

    public static string Expand(string expression, IReadOnlyDictionary<string, string>? customPresets = null)
    {
        var presets = new Dictionary<string, string>(BuiltIn, StringComparer.OrdinalIgnoreCase);
        if (customPresets is not null)
        {
            foreach (var (key, value) in customPresets)
            {
                presets[key] = value;
            }
        }

        var parts = expression.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var expanded = parts.Select(part =>
        {
            if (part.StartsWith('!'))
            {
                var inner = part[1..];
                return presets.TryGetValue(inner, out var preset) ? "!" + preset : part;
            }

            return presets.TryGetValue(part, out var value) ? value : part;
        });

        return string.Join("|", expanded);
    }
}