using System.Text.RegularExpressions;
using TorrentBot.Contracts.Query;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Query;

public sealed class DuckDbQueryCompiler
{
    private static readonly HashSet<string> Operators =
    [
        "eq", "=", "neq", "!=", "contains", "not_contains", "fuzzy_contains",
        "in", "not_in", "gt", "gte", "lt", "lte", "between", "exists"
    ];

    public QuerySpec Sanitize(IReadOnlyDictionary<string, object?> raw, QuerySourceMeta sourceMeta)
    {
        var source = (raw.TryGetValue("source", out var sourceValue) ? sourceValue?.ToString() : sourceMeta.Name) ?? sourceMeta.Name;
        if (!string.Equals(source, sourceMeta.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new QueryValidationException($"Query source mismatch: {source} != {sourceMeta.Name}");
        }

        var fields = sourceMeta.Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);
        var where = new List<QueryWhere>();
        if (raw.TryGetValue("where", out var whereValue) && whereValue is IEnumerable<object> whereItems)
        {
            foreach (var item in whereItems)
            {
                if (item is not IReadOnlyDictionary<string, object?> dict)
                {
                    continue;
                }

                var field = dict.TryGetValue("field", out var fieldValue) ? fieldValue?.ToString() : null;
                var op = NormalizeOp(dict.TryGetValue("op", out var opValue) ? opValue?.ToString() : "eq");
                if (string.IsNullOrWhiteSpace(field) || !fields.ContainsKey(field) || !Operators.Contains(op))
                {
                    continue;
                }

                dict.TryGetValue("value", out var value);
                where.Add(new QueryWhere(field, op, value));
            }
        }

        var select = new List<string>();
        if (raw.TryGetValue("select", out var selectValue) && selectValue is IEnumerable<object> selectItems)
        {
            select.AddRange(selectItems.Select(s => s?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s) && fields.ContainsKey(s!))!);
        }

        if (select.Count == 0)
        {
            select.AddRange(fields.Keys.Take(8));
        }

        var orderBy = new List<QueryOrderBy>();
        if (raw.TryGetValue("order_by", out var orderValue) && orderValue is IEnumerable<object> orderItems)
        {
            foreach (var item in orderItems)
            {
                if (item is not IReadOnlyDictionary<string, object?> dict)
                {
                    continue;
                }

                var field = dict.TryGetValue("field", out var f) ? f?.ToString() : null;
                if (string.IsNullOrWhiteSpace(field) || !fields.ContainsKey(field))
                {
                    continue;
                }

                var direction = dict.TryGetValue("direction", out var d) ? d?.ToString() : "asc";
                orderBy.Add(new QueryOrderBy(field, string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase)));
            }
        }

        var limit = 10;
        if (raw.TryGetValue("limit", out var limitValue) && int.TryParse(limitValue?.ToString(), out var parsed))
        {
            limit = parsed;
        }

        limit = Math.Clamp(limit, 1, sourceMeta.MaxLimit);
        return new QuerySpec(sourceMeta.Name, where, select, orderBy, Limit: limit);
    }

    public (string Sql, List<object?> Parameters) CompileSelect(QuerySpec spec)
    {
        var parameters = new List<object?>();
        var selectClause = string.Join(", ", spec.Select.Select(Quote));
        var whereClause = CompileWhere(spec.Where, parameters);
        var sql = $"SELECT {selectClause} FROM query_rows";
        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            sql += $" WHERE {whereClause}";
        }

        if (spec.OrderBy.Count > 0)
        {
            sql += " ORDER BY " + string.Join(", ",
                spec.OrderBy.Select(o => $"{Quote(o.Field)} {(o.Descending ? "DESC" : "ASC")}"));
        }

        sql += " LIMIT ?";
        parameters.Add(spec.Limit);
        return (sql, parameters);
    }

    private static string CompileWhere(IReadOnlyList<QueryWhere> where, List<object?> parameters)
    {
        var parts = new List<string>();
        foreach (var item in where)
        {
            var field = Quote(item.Field);
            var op = NormalizeOp(item.Op);
            switch (op)
            {
                case "exists":
                    parts.Add($"{field} IS NOT NULL");
                    break;
                case "eq":
                case "=":
                    parts.Add($"{field} = ?");
                    parameters.Add(item.Value);
                    break;
                case "neq":
                case "!=":
                    parts.Add($"{field} != ?");
                    parameters.Add(item.Value);
                    break;
                case "contains":
                    parts.Add($"LOWER(CAST({field} AS VARCHAR)) LIKE ?");
                    parameters.Add($"%{item.Value?.ToString()?.ToLowerInvariant()}%");
                    break;
                case "not_contains":
                    parts.Add($"LOWER(CAST({field} AS VARCHAR)) NOT LIKE ?");
                    parameters.Add($"%{item.Value?.ToString()?.ToLowerInvariant()}%");
                    break;
                case "fuzzy_contains":
                    parts.Add($"LOWER(REPLACE(REPLACE(REPLACE(CAST({field} AS VARCHAR), '-', ''), '.', ''), ' ', '')) LIKE ?");
                    parameters.Add($"%{NormalizeText(item.Value)}%");
                    break;
                case "gt":
                    parts.Add($"{field} > ?");
                    parameters.Add(item.Value);
                    break;
                case "gte":
                    parts.Add($"{field} >= ?");
                    parameters.Add(item.Value);
                    break;
                case "lt":
                    parts.Add($"{field} < ?");
                    parameters.Add(item.Value);
                    break;
                case "lte":
                    parts.Add($"{field} <= ?");
                    parameters.Add(item.Value);
                    break;
                case "in":
                    var values = item.Value as IEnumerable<object> ?? [item.Value];
                    var list = values.ToList();
                    parts.Add($"{field} IN ({string.Join(", ", Enumerable.Repeat("?", list.Count))})");
                    parameters.AddRange(list);
                    break;
                case "not_in":
                    var excluded = item.Value as IEnumerable<object> ?? [item.Value];
                    var excludedList = excluded.ToList();
                    parts.Add($"{field} NOT IN ({string.Join(", ", Enumerable.Repeat("?", excludedList.Count))})");
                    parameters.AddRange(excludedList);
                    break;
                case "between":
                    var bounds = item.Value as IEnumerable<object> ?? [];
                    var boundList = bounds.ToList();
                    if (boundList.Count >= 2)
                    {
                        parts.Add($"TRY_CAST({field} AS DOUBLE) BETWEEN TRY_CAST(? AS DOUBLE) AND TRY_CAST(? AS DOUBLE)");
                        parameters.Add(boundList[0]);
                        parameters.Add(boundList[1]);
                    }
                    break;
            }
        }

        return string.Join(" AND ", parts);
    }

    private static string NormalizeOp(string? op) => (op ?? "eq").Trim().ToLowerInvariant();

    private static string Quote(string field) => $"\"{field.Replace("\"", "\"\"")}\"";

    private static string NormalizeText(object? value) =>
        Regex.Replace(value?.ToString() ?? string.Empty, "[^a-zA-Z0-9]+", string.Empty).ToLowerInvariant();
}