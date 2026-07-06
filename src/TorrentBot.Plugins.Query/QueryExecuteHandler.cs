using System.Collections;
using System.Text.Json;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Query;

namespace TorrentBot.Plugins.Query;

public sealed class QueryExecuteHandler : ICapabilityHandler
{
    public async Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var source = parameters.TryGetValue("source", out var sourceValue) ? sourceValue?.ToString() : null;
        if (string.IsNullOrWhiteSpace(source))
        {
            return new CapabilityResult(Success: false, Message: "Parameter 'source' is required.", IsDryRun: context.IsDryRun);
        }

        var spec = BuildSpec(parameters, source);
        var result = await context.Engine.QueryAsync(source, spec, cancellationToken).ConfigureAwait(false);

        return new CapabilityResult(
            Success: true,
            Data: new Dictionary<string, object?>
            {
                ["source"] = result.Source,
                ["count"] = result.Count,
                ["items"] = result.Items,
                ["summary"] = result.Summary
            },
            Message: $"Query returned {result.Count} item(s) from '{result.Source}'",
            IsDryRun: context.IsDryRun);
    }

    private static QuerySpec BuildSpec(IReadOnlyDictionary<string, object?> parameters, string source)
    {
        if (parameters.TryGetValue("spec", out var specValue) && specValue is JsonElement json)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json.GetRawText());
            if (dict is not null)
            {
                return new QuerySpec(
                    source,
                    ParseWhere(dict),
                    ParseSelect(dict),
                    Limit: ParseLimit(dict));
            }
        }

        return new QuerySpec(
            source,
            ParseWhere(parameters),
            ParseSelect(parameters),
            Limit: ParseLimit(parameters));
    }

    private static IReadOnlyList<QueryWhere>? ParseWhere(IReadOnlyDictionary<string, object?> raw)
    {
        if (!raw.TryGetValue("where", out var value))
        {
            return null;
        }

        return value switch
        {
            JsonElement json when json.ValueKind == JsonValueKind.Array => json.EnumerateArray()
                .Select(ParseWhereFromJson)
                .Where(w => w is not null)
                .Cast<QueryWhere>()
                .ToList(),
            IEnumerable<QueryWhere> list => list.ToList(),
            IEnumerable enumerable => ParseWhereFromEnumerable(enumerable),
            _ => null
        };
    }

    private static IReadOnlyList<QueryWhere>? ParseWhereFromEnumerable(IEnumerable enumerable)
    {
        var result = new List<QueryWhere>();
        foreach (var item in enumerable)
        {
            var clause = item switch
            {
                QueryWhere where => where,
                IReadOnlyDictionary<string, object?> dict => ParseWhereClause(dict),
                JsonElement json => ParseWhereFromJson(json),
                _ => null
            };

            if (clause is not null)
            {
                result.Add(clause);
            }
        }

        return result.Count > 0 ? result : null;
    }

    private static QueryWhere? ParseWhereFromJson(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var field = item.TryGetProperty("field", out var fieldElement) ? fieldElement.GetString() : null;
        var op = item.TryGetProperty("op", out var opElement) ? opElement.GetString() : "eq";
        object? value = item.TryGetProperty("value", out var valueElement)
            ? JsonSerializer.Deserialize<object>(valueElement.GetRawText())
            : null;

        return string.IsNullOrWhiteSpace(field)
            ? null
            : new QueryWhere(field, op ?? "eq", value);
    }

    private static QueryWhere ParseWhereClause(IReadOnlyDictionary<string, object?> dict)
    {
        var field = dict.TryGetValue("field", out var fieldValue) ? fieldValue?.ToString() : string.Empty;
        var op = dict.TryGetValue("op", out var opValue) ? opValue?.ToString() : "eq";
        dict.TryGetValue("value", out var value);
        return new QueryWhere(field ?? string.Empty, op ?? "eq", value);
    }

    private static IReadOnlyList<string>? ParseSelect(IReadOnlyDictionary<string, object?> raw)
    {
        if (!raw.TryGetValue("select", out var value))
        {
            return null;
        }

        return value switch
        {
            JsonElement json when json.ValueKind == JsonValueKind.Array => json.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList(),
            IEnumerable<string> list => list.ToList(),
            IEnumerable enumerable => enumerable.Cast<object?>().Select(v => v?.ToString() ?? string.Empty).ToList(),
            _ => null
        };
    }

    private static int ParseLimit(IReadOnlyDictionary<string, object?> raw)
    {
        if (raw.TryGetValue("limit", out var value) && int.TryParse(value?.ToString(), out var limit))
        {
            return limit;
        }

        return 20;
    }
}