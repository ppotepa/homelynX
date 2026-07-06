namespace TorrentBot.Bootstrap;

public static class HomelynxEnv
{
    public static string? GetServiceUrl(
        string? directUrl,
        string hostVariable,
        string portVariable,
        string httpsVariable,
        string defaultScheme = "http")
    {
        if (!string.IsNullOrWhiteSpace(directUrl))
        {
            return directUrl.Trim();
        }

        var host = Environment.GetEnvironmentVariable(hostVariable);
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var port = Environment.GetEnvironmentVariable(portVariable);
        var scheme = string.Equals(Environment.GetEnvironmentVariable(httpsVariable), "true", StringComparison.OrdinalIgnoreCase)
            ? "https"
            : defaultScheme;

        return string.IsNullOrWhiteSpace(port)
            ? $"{scheme}://{host.Trim()}"
            : $"{scheme}://{host.Trim()}:{port.Trim()}";
    }

    public static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}