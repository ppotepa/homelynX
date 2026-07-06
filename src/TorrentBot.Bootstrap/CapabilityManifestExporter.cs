using System.Text.Json;
using TorrentBot.Acl;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;
using TorrentBot.Engine;

namespace TorrentBot.Bootstrap;

public static class CapabilityManifestExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task ExportIfConfiguredAsync(EngineHost engine, CancellationToken cancellationToken = default)
    {
        var path = Environment.GetEnvironmentVariable("HOMELYNX_CAPABILITIES_FILE");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var result = await engine.SubmitAsync(
            new Invocation
            {
                IsExplicit = true,
                CapabilityName = "system.capabilities",
                RequestContext = new RequestContext(
                    Guid.NewGuid().ToString("N"),
                    Guid.NewGuid().ToString("N"),
                    "admin",
                    source: "bootstrap"),
                User = AclService.FromEnvironment().ResolveUser("admin")
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.Success || result.CapabilityResult?.Data is not Dictionary<string, object?> data)
        {
            return;
        }

        var capabilities = data.TryGetValue("capabilities", out var raw)
            ? raw as IEnumerable<Dictionary<string, object?>>
            : null;

        var grouped = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);
        if (capabilities is not null)
        {
            foreach (var capability in capabilities)
            {
                var name = capability.TryGetValue("name", out var nameValue) ? nameValue?.ToString() : null;
                var module = string.IsNullOrWhiteSpace(name)
                    ? "unknown"
                    : name!.Split('.', 2)[0];
                if (!grouped.TryGetValue(module, out var bucket))
                {
                    bucket = [];
                    grouped[module] = bucket;
                }

                bucket.Add(capability);
            }
        }

        var payload = new Dictionary<string, object?>
        {
            ["success"] = true,
            ["count"] = data.TryGetValue("count", out var count) ? count : grouped.Values.Sum(g => g.Count),
            ["groups"] = grouped,
            ["source"] = "homelynx-csharp"
        };

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken)
            .ConfigureAwait(false);
    }
}