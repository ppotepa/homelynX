using DuckDB.NET.Data;
using TorrentBot.Contracts.Query;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Query;

public sealed class DuckDbQueryEngine
{
    private readonly DuckDbQueryCompiler _compiler = new();

    public QueryResult Execute(QuerySourceMeta meta, IReadOnlyList<Dictionary<string, object?>> rows, QuerySpec spec)
    {
        using var connection = new DuckDBConnection("Data Source=:memory:");
        connection.Open();

        var columns = meta.Fields.Select(f => f.Name).ToList();
        if (columns.Count == 0)
        {
            columns.AddRange(rows.SelectMany(r => r.Keys).Distinct());
        }

        if (columns.Count > 0)
        {
            var columnDefs = string.Join(", ", columns.Select(c => $"{Quote(c)} VARCHAR"));
            using (var create = connection.CreateCommand())
            {
                create.CommandText = $"CREATE TABLE query_rows ({columnDefs})";
                create.ExecuteNonQuery();
            }

            foreach (var row in rows)
            {
                using var insert = connection.CreateCommand();
                var paramNames = columns.Select((_, i) => $"${i + 1}").ToList();
                insert.CommandText = $"INSERT INTO query_rows ({string.Join(", ", columns.Select(Quote))}) VALUES ({string.Join(", ", paramNames)})";
                foreach (var column in columns)
                {
                    insert.Parameters.Add(new DuckDBParameter(row.TryGetValue(column, out var value) ? value : null));
                }

                insert.ExecuteNonQuery();
            }
        }
        else
        {
            using var create = connection.CreateCommand();
            create.CommandText = "CREATE TABLE query_rows (placeholder VARCHAR)";
            create.ExecuteNonQuery();
        }

        var sanitized = _compiler.Sanitize(ToRaw(spec), meta);
        var (sql, parameters) = _compiler.CompileSelect(sanitized);

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new DuckDBParameter(parameter));
        }

        var items = new List<Dictionary<string, object?>>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var item = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                item[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            items.Add(item);
        }

        return new QueryResult(meta.Name, items, items.Count, Summary: sql);
    }

    public QueryResult Execute(QuerySourceMeta meta, IReadOnlyList<Dictionary<string, object?>> rows, IReadOnlyDictionary<string, object?> raw) =>
        Execute(meta, rows, _compiler.Sanitize(raw, meta));

    private static Dictionary<string, object?> ToRaw(QuerySpec spec) => new()
    {
        ["source"] = spec.Source,
        ["where"] = spec.Where?.Select(w => new Dictionary<string, object?> { ["field"] = w.Field, ["op"] = w.Op, ["value"] = w.Value }).ToArray(),
        ["select"] = spec.Select?.ToArray(),
        ["order_by"] = spec.OrderBy?.Select(o => new Dictionary<string, object?> { ["field"] = o.Field, ["direction"] = o.Descending ? "desc" : "asc" }).ToArray(),
        ["limit"] = spec.Limit
    };

    private static string Quote(string field) => $"\"{field.Replace("\"", "\"\"")}\"";
}